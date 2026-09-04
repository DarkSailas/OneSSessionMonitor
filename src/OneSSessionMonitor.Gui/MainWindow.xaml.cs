using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using OneSSessionMonitor.Core.Models;
using OneSSessionMonitor.Core.Ras;
using OneSSessionMonitor.Core.Services;
using OneSSessionMonitor.Core.Slk;
using OneSSessionMonitor.Core.State;

namespace OneSSessionMonitor.Gui;

public sealed class GuiSessionState
{
    public string SearchFilter { get; set; } = string.Empty;
    public string SelectedBase { get; set; } = "Все базы";
    public string SelectedApp { get; set; } = "Все клиенты";
    public string SelectedStatusFilter { get; set; } = "ALL";
    public bool IsDryRun { get; set; } = false;
    public bool ShowAllUsers { get; set; } = false;
}

public partial class MainWindow : Window
{
    private CancellationTokenSource? _scanCts;
    private readonly ISessionMonitorService _cleanerService;
    private readonly ISlkClient _slkClient;
    private readonly CleanerState _cleanerState;
    private readonly IConfiguration _configuration;
    private SessionMonitorOptions _options;
    private bool _isSettingsDirty = false;

    private readonly List<V8SessionInfo> _allSessions = [];
    private readonly ObservableCollection<V8SessionInfo> _viewSessions = [];

    private string _currentStatusFilter = "ALL"; // "ALL", "SLEEPING", "FROZEN", "ACTIVE", "SLK"
    private readonly string _stateFilePath = Path.Combine(AppContext.BaseDirectory, "gui_state.json");

    public MainWindow()
    {
        InitializeComponent();
        TxtAppVersion.Text = $"v{typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.4.0"}";
        TxtAppBuildDate.Text = GetAppBuildTimestamp();

        _cleanerService = App.ServiceProvider.GetRequiredService<ISessionMonitorService>();
        _slkClient = App.ServiceProvider.GetRequiredService<ISlkClient>();
        _cleanerState = App.ServiceProvider.GetRequiredService<CleanerState>();
        _configuration = App.ServiceProvider.GetRequiredService<IConfiguration>();
        _options = App.ServiceProvider.GetRequiredService<IOptions<SessionMonitorOptions>>().Value;

        GridSessions.ItemsSource = _viewSessions;
        RacProcessExecutor.OnRacLog += HandleRacLogMessage;

        LoadSettingsToUi();
        RestoreGuiSessionState();
        HookUpSettingsDirtyTracking();

        Closing += MainWindow_Closing;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        SaveGuiSessionState();
    }

    private void RestoreGuiSessionState()
    {
        try
        {
            if (File.Exists(_stateFilePath))
            {
                string json = File.ReadAllText(_stateFilePath);
                var state = JsonSerializer.Deserialize<GuiSessionState>(json);
                if (state != null)
                {
                    TxtSearchFilter.Text = state.SearchFilter ?? string.Empty;
                    _currentStatusFilter = state.SelectedStatusFilter ?? "ALL";
                    ChkDryRun.IsChecked = state.IsDryRun;
                    TglShowAllUsers.IsChecked = state.ShowAllUsers;

                    ChipAll.IsChecked = _currentStatusFilter == "ALL";
                    ChipSleeping.IsChecked = _currentStatusFilter == "SLEEPING";
                    ChipFrozen.IsChecked = _currentStatusFilter == "FROZEN";
                    ChipActive.IsChecked = _currentStatusFilter == "ACTIVE";
                    ChipSlk.IsChecked = _currentStatusFilter == "SLK";
                }
            }
        }
        catch { }
    }

    private void SaveGuiSessionState()
    {
        try
        {
            var state = new GuiSessionState
            {
                SearchFilter = TxtSearchFilter.Text,
                SelectedBase = CmbBaseFilter?.SelectedItem as string ?? "Все базы",
                SelectedApp = CmbAppFilter?.SelectedItem as string ?? "Все клиенты",
                SelectedStatusFilter = _currentStatusFilter,
                IsDryRun = ChkDryRun.IsChecked == true,
                ShowAllUsers = TglShowAllUsers.IsChecked == true
            };
            string json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_stateFilePath, json);
        }
        catch { }
    }

    private void LoadSettingsToUi()
    {
        CfgServer.Text = _options.Server;
        CfgClusterAdminUser.Text = _options.ClusterAdminUser ?? string.Empty;
        CfgClusterAdminPassword.Password = _options.ClusterAdminPassword ?? string.Empty;
        CfgRacPath.Text = _options.RacPath ?? string.Empty;
        CfgSlkServer.Text = _options.SlkServerEndpoint ?? string.Empty;
        CfgSlkUser.Text = _options.SlkUser ?? string.Empty;
        CfgSlkPassword.Password = _options.SlkPassword ?? string.Empty;
        CfgOnlyHibernate.IsChecked = _options.OnlyHibernate;
        CfgMinMinutes.Text = _options.MinHibernateMinutes.ToString();
        CfgCleanFrozen.IsChecked = _options.CleanFrozenSessions;
        CfgMaxDbProcMinutes.Text = _options.MaxDbProcMinutes.ToString();
        CfgMaxCallDurationMinutes.Text = _options.MaxCallDurationMinutes.ToString();
        CfgExcludedUsers.Text = string.Join(", ", _options.ExcludedUsers.Distinct(StringComparer.OrdinalIgnoreCase));
        CfgExcludedBases.Text = string.Join(", ", _options.ExcludedInfoBases.Distinct(StringComparer.OrdinalIgnoreCase));
        CfgExcludedAppIds.Text = string.Join(", ", _options.ExcludedAppIds.Count > 0 
            ? _options.ExcludedAppIds.Distinct(StringComparer.OrdinalIgnoreCase) 
            : ["Designer"]);

        TxtServerEndpoint.Text = _options.Server;
        SetSettingsDirty(false);
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshSessionsInternalAsync();
    }

    private async Task RefreshSessionsInternalAsync()
    {
        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;

        BtnRefresh.IsEnabled = false;
        BtnCleanSleeping.IsEnabled = false;
        BtnTerminateSelected.IsEnabled = false;
        if (BtnCancelScan != null) BtnCancelScan.Visibility = Visibility.Visible;
        TxtStatusMessage.Text = "Опрос кластеров 1С через протокол RAS и сервера СЛК...";
        DotStatus.Fill = new SolidColorBrush(Color.FromRgb(0xF9, 0x73, 0x16)); // Orange

        try
        {
            _allSessions.Clear();
            _viewSessions.Clear();

            // 1. Опрос сервера СЛК
            var slkTask = _slkClient.GetSlkStatusAsync(_options.SlkServerEndpoint, _options.SlkUser, _options.SlkPassword, token);

            // 2. Опрос серверов 1С через RAS
            var endpoints = _options.GetEndpoints();
            int count = 0;
            int sleeping = 0;
            int frozen = 0;
            int industryLic = 0;

            var rawSessions = new List<V8SessionInfo>();
            await foreach (var session in _cleanerService.DiscoverSessionsAsync(endpoints, token))
            {
                rawSessions.Add(session);
            }

            var slkStatus = await slkTask;
            
            // Точное сопоставление сеансов с лицензиями СЛК
            foreach (var session in rawSessions)
            {
                bool hasSlk = session.HasIndustryLicense;
                if (slkStatus.IsConnected && slkStatus.ActiveSessions.Count > 0)
                {
                    hasSlk = slkStatus.ActiveSessions.Any(s => 
                        (s.SessionId.HasValue && s.SessionId.Value == session.SessionId) ||
                        (!string.IsNullOrWhiteSpace(s.UserName) && !string.IsNullOrWhiteSpace(session.UserName) && 
                         (session.UserName.Contains(s.UserName, StringComparison.OrdinalIgnoreCase) || s.UserName.Contains(session.UserName, StringComparison.OrdinalIgnoreCase)))
                    );
                }

                var finalSession = session with { HasIndustryLicense = hasSlk };
                _allSessions.Add(finalSession);

                count++;
                if (finalSession.Hibernate) sleeping++;
                if (finalSession.IsFrozen) frozen++;
                if (finalSession.HasIndustryLicense) industryLic++;
            }

            if (slkStatus.IsConnected && slkStatus.TotalLicenses > 0)
            {
                TxtIndustryLicCount.Text = $"{slkStatus.InUseLicenses} / {slkStatus.TotalLicenses}";
                TxtSlkStatusSub.Text = $"Свободно: {slkStatus.FreeLicenses} (Ключей: {slkStatus.TotalKeys})";
            }
            else if (slkStatus.IsConnected)
            {
                TxtIndustryLicCount.Text = $"{slkStatus.InUseLicenses}";
                TxtSlkStatusSub.Text = $"СЛК активен (Ключей: {slkStatus.TotalKeys})";
            }
            else
            {
                TxtIndustryLicCount.Text = industryLic > 0 ? industryLic.ToString() : "—";
                TxtSlkStatusSub.Text = "СЛК недоступен";
            }

            TxtTotalSessions.Text = count.ToString();
            TxtSleepingSessions.Text = sleeping.ToString();
            TxtFrozenSessions.Text = frozen.ToString();

            TxtStatusMessage.Text = $"Сканирование завершено. Всего: {count}, Спящих: {sleeping}, Зависших: {frozen}, СЛК: {industryLic}.";
            DotStatus.Fill = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)); // Green

            PopulateDropdowns();
            UpdateChipCounters();
            ApplyFilter();
        }
        catch (OperationCanceledException)
        {
            TxtStatusMessage.Text = "Сканирование остановлено пользователем.";
            DotStatus.Fill = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA));
        }
        catch (Exception ex)
        {
            TxtStatusMessage.Text = "Ошибка при опросе серверов RAS.";
            DotStatus.Fill = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)); // Red
            MessageBox.Show(this, $"Ошибка опроса серверов RAS:\n{ex.Message}", "Ошибка подключения к RAS", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnRefresh.IsEnabled = true;
            BtnCleanSleeping.IsEnabled = true;
            BtnTerminateSelected.IsEnabled = true;
            if (BtnCancelScan != null) BtnCancelScan.Visibility = Visibility.Collapsed;
        }
    }

    private void BtnCancelScan_Click(object sender, RoutedEventArgs e)
    {
        _scanCts?.Cancel();
        TxtStatusMessage.Text = "Остановка сканирования...";
        DotStatus.Fill = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
    }

    private void PopulateDropdowns()
    {
        var currentBase = CmbBaseFilter.SelectedItem as string;
        var currentApp = CmbAppFilter.SelectedItem as string;

        var bases = new List<string> { "Все базы" };
        bases.AddRange(_allSessions.Select(s => s.InfoBaseName).Where(b => !string.IsNullOrEmpty(b)).Distinct().OrderBy(b => b));
        CmbBaseFilter.ItemsSource = bases;
        CmbBaseFilter.SelectedItem = bases.Contains(currentBase ?? "") ? currentBase : "Все базы";

        var apps = new List<string> { "Все клиенты" };
        apps.AddRange(_allSessions.Select(s => s.AppId).Where(a => !string.IsNullOrEmpty(a)).Distinct().OrderBy(a => a));
        CmbAppFilter.ItemsSource = apps;
        CmbAppFilter.SelectedItem = apps.Contains(currentApp ?? "") ? currentApp : "Все клиенты";
    }

    private void UpdateChipCounters()
    {
        int total = _allSessions.Count;
        int sleeping = _allSessions.Count(s => s.Hibernate);
        int frozen = _allSessions.Count(s => s.IsFrozen);
        int active = _allSessions.Count(s => !s.Hibernate && !s.IsFrozen);
        int slk = _allSessions.Count(s => s.HasIndustryLicense);

        ChipAll.Content = $"ВСЕ ({total})";
        ChipSleeping.Content = $"СПЯЩИЕ ({sleeping})";
        ChipFrozen.Content = $"ЗАВИСШИЕ ({frozen})";
        ChipActive.Content = $"АКТИВНЫЕ ({active})";
        ChipSlk.Content = $"СЛК ЛИЦЕНЗИИ ({slk})";
    }

    private async void BtnCleanSleeping_Click(object sender, RoutedEventArgs e)
    {
        bool dryRun = ChkDryRun.IsChecked == true;
        string actionName = dryRun ? "СИМУЛЯЦИЯ ОЧИСТКИ (DRY-RUN)" : "ЗАВЕРШЕНИЕ СПЯЩИХ СЕАНСОВ";

        if (!dryRun)
        {
            var res = MessageBox.Show(this, "Вы действительно хотите завершить спящие сеансы 1С?",
                "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res != MessageBoxResult.Yes) return;
        }

        BtnRefresh.IsEnabled = false;
        BtnCleanSleeping.IsEnabled = false;
        BtnTerminateSelected.IsEnabled = false;
        TxtStatusMessage.Text = $"{actionName} выполняется...";
        DotStatus.Fill = new SolidColorBrush(Color.FromRgb(0xF9, 0x73, 0x16));

        try
        {
            var endpoints = _options.GetEndpoints();
            int minMinutes = int.TryParse(CfgMinMinutes.Text, out var mm) ? mm : _options.MinHibernateMinutes;
            var criteria = _options.GetCriteria() with { MinHibernateDuration = TimeSpan.FromMinutes(minMinutes) };

            RacProcessExecutor.Log($"=== ЗАПУСК ОЧИСТКИ СПЯЩИХ СЕАНСОВ (Порог неактивности: {minMinutes} мин, DryRun: {dryRun}) ===");
            var report = await _cleanerService.ExecuteCleanAsync(endpoints, criteria, dryRun);
            _cleanerState.RecordReport(report);

            RacProcessExecutor.Log($"[ИТОГ ОЧИСТКИ]: Найдено сеансов: {report.TotalSessionsFound}, Спящих: {report.TotalSleepingSessions}, Отобрано по критериям: {report.FilteredForTerminationCount}, Завершено: {report.SuccessfullyTerminatedCount}, Ошибок: {report.FailedTerminationsCount}");

            if (report.FilteredForTerminationCount == 0)
            {
                TxtStatusMessage.Text = $"Нет спящих сеансов, подходящих под критерии (порог сна: {minMinutes} мин). Уменьшите порог в Настройках.";
                DotStatus.Fill = new SolidColorBrush(Color.FromRgb(0xF9, 0x73, 0x16));
            }
            else
            {
                TxtStatusMessage.Text = $"Очистка завершена. Отобрано: {report.FilteredForTerminationCount}, Завершено: {report.SuccessfullyTerminatedCount}, Ошибок: {report.FailedTerminationsCount}";
                DotStatus.Fill = report.FailedTerminationsCount > 0
                    ? new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44))
                    : new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
            }

            // Автоматическое мгновенное обновление таблицы
            await RefreshSessionsInternalAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Ошибка выполнения очистки:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnRefresh.IsEnabled = true;
            BtnCleanSleeping.IsEnabled = true;
            BtnTerminateSelected.IsEnabled = true;
        }
    }

    private async void BtnTerminateSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = GridSessions.SelectedItems.Cast<V8SessionInfo>().ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Выберите один или несколько сеансов в таблице для завершения.", "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(this, $"Вы действительно хотите ПРИНУДИТЕЛЬНО завершить выбранные сеансы ({selected.Count} шт.) независимо от их статуса?", "Принудительное завершение", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        BtnRefresh.IsEnabled = false;
        BtnCleanSleeping.IsEnabled = false;
        BtnTerminateSelected.IsEnabled = false;
        TxtStatusMessage.Text = "Завершение выбранных сеансов...";
        DotStatus.Fill = new SolidColorBrush(Color.FromRgb(0xF9, 0x73, 0x16));

        int success = 0;
        int failed = 0;

        try
        {
            RacProcessExecutor.Log($"=== ЗАПУСК ЗАВЕРШЕНИЯ ВЫБРАННЫХ СЕАНСОВ ({selected.Count} шт.) ===");

            foreach (var s in selected)
            {
                RacProcessExecutor.Log($"[ВЫБРАННЫЙ СЕАНС] ID: {s.SessionId}, UUID: {s.SessionUuid}, Пользователь: '{s.UserName}', База: '{s.InfoBaseName}', Сервер: '{s.Server}', Кластер: '{s.ClusterName}' ({s.ClusterId})");

                var server = OneCServerEndpoint.Parse(s.Server) with
                {
                    ClusterAdminUser = _options.ClusterAdminUser,
                    ClusterAdminPassword = _options.ClusterAdminPassword,
                    RacPath = _options.RacPath
                };
                var cluster = new V8ClusterInfo(s.ClusterId, s.ClusterName, server.Host, server.ClusterPort, _options.ClusterAdminUser, _options.ClusterAdminPassword);

                bool ok = await _cleanerService.TerminateSingleSessionAsync(server, cluster, s);
                if (ok)
                {
                    success++;
                    RacProcessExecutor.Log($"[УСПЕХ] Сеанс ID {s.SessionId} ({s.UserName}) завершен.");
                }
                else
                {
                    failed++;
                    RacProcessExecutor.Log($"[ОШИБКА] Не удалось завершить сеанс ID {s.SessionId} ({s.UserName}).");
                }
            }

            TxtStatusMessage.Text = $"Завершено выбранных сеансов: {success}, Ошибок: {failed}";
            DotStatus.Fill = failed > 0
                ? new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44))
                : new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));

            // Автоматическое мгновенное обновление таблицы
            await RefreshSessionsInternalAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Ошибка завершения сеансов:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnRefresh.IsEnabled = true;
            BtnCleanSleeping.IsEnabled = true;
            BtnTerminateSelected.IsEnabled = true;
        }
    }

    private void ChipStatus_Click(object sender, RoutedEventArgs e)
    {
        ChipAll.IsChecked = sender == ChipAll;
        ChipSleeping.IsChecked = sender == ChipSleeping;
        ChipFrozen.IsChecked = sender == ChipFrozen;
        ChipActive.IsChecked = sender == ChipActive;
        ChipSlk.IsChecked = sender == ChipSlk;

        if (sender == ChipAll) _currentStatusFilter = "ALL";
        else if (sender == ChipSleeping) _currentStatusFilter = "SLEEPING";
        else if (sender == ChipFrozen) _currentStatusFilter = "FROZEN";
        else if (sender == ChipActive) _currentStatusFilter = "ACTIVE";
        else if (sender == ChipSlk) _currentStatusFilter = "SLK";

        ApplyFilter();
        SaveGuiSessionState();
    }

    private void FilterChanged_Handler(object sender, EventArgs e)
    {
        ApplyFilter();
        SaveGuiSessionState();
    }

    private void BtnResetFilters_Click(object sender, RoutedEventArgs e)
    {
        TxtSearchFilter.Text = string.Empty;
        _currentStatusFilter = "ALL";
        ChipAll.IsChecked = true;
        ChipSleeping.IsChecked = false;
        ChipFrozen.IsChecked = false;
        ChipActive.IsChecked = false;
        ChipSlk.IsChecked = false;
        TglShowAllUsers.IsChecked = false;
        if (CmbBaseFilter.Items.Count > 0) CmbBaseFilter.SelectedIndex = 0;
        if (CmbAppFilter.Items.Count > 0) CmbAppFilter.SelectedIndex = 0;
        ApplyFilter();
        SaveGuiSessionState();
    }

    private void ApplyFilter()
    {
        _viewSessions.Clear();
        string filter = TxtSearchFilter.Text.Trim();
        string selectedBase = CmbBaseFilter?.SelectedItem as string ?? "Все базы";
        string selectedApp = CmbAppFilter?.SelectedItem as string ?? "Все клиенты";
        bool showAllUsers = TglShowAllUsers.IsChecked == true;

        // Фильтрация и сортировка: Зависшие (0) -> Спящие (1) -> Активные (2)
        var query = _allSessions.AsEnumerable();

        if (!showAllUsers && _options.ExcludedUsers.Count > 0)
        {
            var exclSet = new HashSet<string>(_options.ExcludedUsers, StringComparer.OrdinalIgnoreCase);
            // Пользователи из белого списка скрываются, но сеансы Конфигуратора (Designer) всегда остаются видимыми для ручного контроля
            query = query.Where(s => !exclSet.Contains(s.UserName) || string.Equals(s.AppId, "Designer", StringComparison.OrdinalIgnoreCase));
        }

        if (_currentStatusFilter == "SLEEPING") query = query.Where(s => s.Hibernate);
        else if (_currentStatusFilter == "FROZEN") query = query.Where(s => s.IsFrozen);
        else if (_currentStatusFilter == "ACTIVE") query = query.Where(s => !s.Hibernate && !s.IsFrozen);
        else if (_currentStatusFilter == "SLK") query = query.Where(s => s.HasIndustryLicense);

        if (selectedBase != "Все базы")
        {
            query = query.Where(s => string.Equals(s.InfoBaseName, selectedBase, StringComparison.OrdinalIgnoreCase));
        }

        if (selectedApp != "Все клиенты")
        {
            query = query.Where(s => string.Equals(s.AppId, selectedApp, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(filter))
        {
            query = query.Where(s => (s.UserName?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                                     (s.InfoBaseName?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                                     (s.Server?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                                     (s.AppId?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                                     (s.Host?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                                     (s.Licenses?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                                     s.SessionId.ToString().Contains(filter));
        }

        // Сортировка по умолчанию: Сначала Зависшие и Спящие, затем по ID
        var sorted = query
            .OrderBy(s => s.StatusSortOrder)
            .ThenByDescending(s => s.HibernateDurationSeconds ?? 0)
            .ThenBy(s => s.SessionId)
            .ToList();

        foreach (var s in sorted)
        {
            _viewSessions.Add(s);
        }

        TxtFilteredStatus.Text = $"Показано: {_viewSessions.Count} из {_allSessions.Count}";
    }

    #region Export Functions (JSONL & Excel)

    private async void BtnExportJson_Click(object sender, RoutedEventArgs e)
    {
        var selected = GridSessions.SelectedItems.OfType<V8SessionInfo>().ToList();
        var isSelected = selected.Count > 0;
        var itemsToExport = isSelected ? selected : _viewSessions.ToList();

        if (itemsToExport.Count == 0)
        {
            MessageBox.Show(this, "Нет данных для экспорта! Сначала выполните сканирование сеансов.", "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var saveDialog = new SaveFileDialog
        {
            Title = isSelected ? $"Экспорт {itemsToExport.Count} выбранных сеансов в JSON" : $"Экспорт таблицы ({itemsToExport.Count} сеансов) в JSON",
            Filter = "JSON Lines файлы (*.jsonl)|*.jsonl|Форматированный JSON (*.json)|*.json|Все файлы (*.*)|*.*",
            FileName = $"ones_sessions_{DateTime.Now:yyyyMMdd_HHmmss}.jsonl"
        };

        if (saveDialog.ShowDialog() == true)
        {
            try
            {
                var isJsonLines = saveDialog.FileName.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase);
                if (isJsonLines)
                {
                    var compactOptions = new JsonSerializerOptions
                    {
                        WriteIndented = false,
                        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
                    };
                    var sb = new StringBuilder();
                    foreach (var item in itemsToExport)
                    {
                        sb.AppendLine(JsonSerializer.Serialize(item, compactOptions));
                    }
                    await File.WriteAllTextAsync(saveDialog.FileName, sb.ToString(), Encoding.UTF8);
                }
                else
                {
                    var prettyOptions = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
                    };
                    var json = JsonSerializer.Serialize(itemsToExport, prettyOptions);
                    await File.WriteAllTextAsync(saveDialog.FileName, json, Encoding.UTF8);
                }

                MessageBox.Show(this, $"Экспорт успешно завершен!\n\n• Сохранено записей: {itemsToExport.Count}\n• Файл: {saveDialog.FileName}", "Экспорт в JSON", MessageBoxButton.OK, MessageBoxImage.Information);
                TxtStatusMessage.Text = $"Экспортировано {itemsToExport.Count} сеансов в {Path.GetFileName(saveDialog.FileName)}.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Ошибка экспорта в JSON:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
    {
        var selected = GridSessions.SelectedItems.OfType<V8SessionInfo>().ToList();
        var isSelected = selected.Count > 0;
        var itemsToExport = isSelected ? selected : _viewSessions.ToList();

        if (itemsToExport.Count == 0)
        {
            MessageBox.Show(this, "Нет данных для экспорта! Сначала выполните сканирование сеансов.", "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var saveDialog = new SaveFileDialog
        {
            Title = isSelected ? $"Экспорт {itemsToExport.Count} выбранных сеансов в Excel" : $"Экспорт таблицы ({itemsToExport.Count} сеансов) в Excel",
            Filter = "Книга Microsoft Excel (*.xlsx)|*.xlsx|Все файлы (*.*)|*.*",
            FileName = $"ones_sessions_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
        };

        if (saveDialog.ShowDialog() == true)
        {
            try
            {
                ExcelExportService.ExportSessionsToExcel(saveDialog.FileName, itemsToExport);
                MessageBox.Show(this, $"Экспорт в Excel успешно завершен!\n\n• Сохранено строк: {itemsToExport.Count}\n• Файл: {saveDialog.FileName}", "Экспорт в Excel", MessageBoxButton.OK, MessageBoxImage.Information);
                TxtStatusMessage.Text = $"Экспортировано {itemsToExport.Count} сеансов в {Path.GetFileName(saveDialog.FileName)}.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Ошибка экспорта в Excel:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    #endregion

    private void BtnCleanMemory_Click(object sender, RoutedEventArgs e)
    {
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        TxtStatusMessage.Text = "Кэш оперативной памяти сброшен.";
    }

    private void TgLink_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://t.me/DarkSailas") { UseShellExecute = true });
        }
        catch { }
    }

    private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _options.Server = CfgServer.Text.Trim();

            var users = CfgExcludedUsers.Text
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .ToList();

            var bases = CfgExcludedBases.Text
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .ToList();

            var appIds = CfgExcludedAppIds.Text
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .ToList();
            if (appIds.Count == 0)
            {
                appIds = ["Designer"];
            }

            _options.Server = CfgServer.Text.Trim();
            _options.ClusterAdminUser = string.IsNullOrWhiteSpace(CfgClusterAdminUser.Text) ? null : CfgClusterAdminUser.Text.Trim();
            _options.ClusterAdminPassword = string.IsNullOrWhiteSpace(CfgClusterAdminPassword.Password) ? null : CfgClusterAdminPassword.Password;
            _options.RacPath = string.IsNullOrWhiteSpace(CfgRacPath.Text) ? null : CfgRacPath.Text.Trim();
            _options.SlkServerEndpoint = string.IsNullOrWhiteSpace(CfgSlkServer.Text) ? null : CfgSlkServer.Text.Trim();
            _options.SlkUser = string.IsNullOrWhiteSpace(CfgSlkUser.Text) ? null : CfgSlkUser.Text.Trim();
            _options.SlkPassword = string.IsNullOrWhiteSpace(CfgSlkPassword.Password) ? null : CfgSlkPassword.Password;
            _options.OnlyHibernate = CfgOnlyHibernate.IsChecked == true;
            _options.MinHibernateMinutes = int.TryParse(CfgMinMinutes.Text, out var mm) ? mm : 15;
            _options.CleanFrozenSessions = CfgCleanFrozen.IsChecked == true;
            _options.MaxDbProcMinutes = int.TryParse(CfgMaxDbProcMinutes.Text, out var mdb) ? mdb : 5;
            _options.MaxCallDurationMinutes = int.TryParse(CfgMaxCallDurationMinutes.Text, out var mcd) ? mcd : 10;
            _options.ExcludedUsers = users;
            _options.ExcludedInfoBases = bases;
            _options.ExcludedAppIds = appIds;

            string configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            string documentedJson = GenerateDocumentedAppSettings(_options);
            File.WriteAllText(configPath, documentedJson, Encoding.UTF8);

            TxtServerEndpoint.Text = _options.Server;
        SetSettingsDirty(false);
            SaveGuiSessionState();
            MessageBox.Show(this, "Параметры успешно сохранены в appsettings.json.", "Успешно", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Ошибка сохранения параметров:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string GenerateDocumentedAppSettings(SessionMonitorOptions opt)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  // ==============================================================================");
        sb.AppendLine("  // КОНФИГУРАЦИЯ СЕРВИСА ОЧИСТКИ СЕАНСОВ 1С:ПРЕДПРИЯТИЕ 8.3 (OneSSessionMonitor)");
        sb.AppendLine("  // Протокол взаимодействия: 100% RAS / RAC (Remote Administration Server)");
        sb.AppendLine("  // ==============================================================================");
        sb.AppendLine("  \"SessionMonitor\": {");
        sb.AppendLine("    // Список серверов RAS (хост:порт)");
        sb.AppendLine($"    \"Server\": \"{opt.Server}\",");
        sb.AppendLine();
        sb.AppendLine("    // Путь к rac.exe (null для автопоиска платформы 1С)");
        sb.AppendLine($"    \"RacPath\": {(opt.RacPath == null ? "null" : $"\"{opt.RacPath}\"")},");
        sb.AppendLine();

        sb.AppendLine("    // Администратор кластера 1С");
        sb.AppendLine($"    \"ClusterAdminUser\": {(opt.ClusterAdminUser == null ? "null" : $"\"{opt.ClusterAdminUser}\"")},");
        sb.AppendLine();
        sb.AppendLine("    // Пароль администратора кластера 1С");
        sb.AppendLine($"    \"ClusterAdminPassword\": {(opt.ClusterAdminPassword == null ? "null" : $"\"{opt.ClusterAdminPassword}\"")},");
        sb.AppendLine();
        sb.AppendLine("    // Адрес сервера СЛК 3.0/2.0 (хост:порт)");
        sb.AppendLine($"    \"SlkServerEndpoint\": {(opt.SlkServerEndpoint == null ? "null" : $"\"{opt.SlkServerEndpoint}\"")},");
        sb.AppendLine();
        sb.AppendLine("    // Завершать спящие сеансы (Hibernate = yes)");
        sb.AppendLine($"    \"OnlyHibernate\": {opt.OnlyHibernate.ToString().ToLowerInvariant()},");
        sb.AppendLine();
        sb.AppendLine("    // Минимальное время неактивности / сна в минутах");
        sb.AppendLine($"    \"MinHibernateMinutes\": {opt.MinHibernateMinutes},");
        sb.AppendLine();
        sb.AppendLine("    // Завершать зависшие сеансы (блокировки СУБД / 1С / зависшие вызовы)");
        sb.AppendLine($"    \"CleanFrozenSessions\": {opt.CleanFrozenSessions.ToString().ToLowerInvariant()},");
        sb.AppendLine();
        sb.AppendLine("    // Порог длительности запроса СУБД в минутах");
        sb.AppendLine($"    \"MaxDbProcMinutes\": {opt.MaxDbProcMinutes},");
        sb.AppendLine();
        sb.AppendLine("    // Порог длительности серверного вызова 1С в минутах");
        sb.AppendLine($"    \"MaxCallDurationMinutes\": {opt.MaxCallDurationMinutes},");
        sb.AppendLine();
        sb.AppendLine("    // Исключенные пользователи (сеансы которых запрещено завершать)");
        sb.AppendLine($"    \"ExcludedUsers\": {JsonSerializer.Serialize(opt.ExcludedUsers)},");
        sb.AppendLine();
        sb.AppendLine("    // Исключенные информационные базы");
        sb.AppendLine($"    \"ExcludedInfoBases\": {JsonSerializer.Serialize(opt.ExcludedInfoBases)},");
        sb.AppendLine();
        sb.AppendLine("    // Исключенные типы клиентских приложений (AppID, защита Конфигуратора 1С)");
        sb.AppendLine($"    \"ExcludedAppIds\": {JsonSerializer.Serialize(opt.ExcludedAppIds.Count > 0 ? opt.ExcludedAppIds : ["Designer"])},");
        sb.AppendLine();
        sb.AppendLine("    // Фильтр по типам клиентских приложений");
        sb.AppendLine($"    \"TargetAppIds\": {JsonSerializer.Serialize(opt.TargetAppIds)},");
        sb.AppendLine();
        sb.AppendLine("    // Регулярные выражения (Regex)");
        sb.AppendLine($"    \"InfoBasePattern\": {(opt.InfoBasePattern == null ? "null" : $"\"{opt.InfoBasePattern}\"")},");
        sb.AppendLine($"    \"UserNamePattern\": {(opt.UserNamePattern == null ? "null" : $"\"{opt.UserNamePattern}\"")},");
        sb.AppendLine();
        sb.AppendLine("    // Интервал запуска для фоновой службы (в секундах)");
        sb.AppendLine($"    \"IntervalSeconds\": {opt.IntervalSeconds},");
        sb.AppendLine();
        sb.AppendLine("    // Режим симуляции (Dry-Run)");
        sb.AppendLine($"    \"DryRun\": {opt.DryRun.ToString().ToLowerInvariant()}");
        sb.AppendLine("  },");
        sb.AppendLine();
        sb.AppendLine("  // Настройки структурированного логирования Serilog");
        sb.AppendLine("  \"Serilog\": {");
        sb.AppendLine("    \"MinimumLevel\": {");
        sb.AppendLine("      \"Default\": \"Error\",");
        sb.AppendLine("      \"Override\": {");
        sb.AppendLine("        \"Microsoft\": \"Error\",");
        sb.AppendLine("        \"System\": \"Error\"");
        sb.AppendLine("      }");
        sb.AppendLine("    },");
        sb.AppendLine("    \"WriteTo\": [");
        sb.AppendLine("      {");
        sb.AppendLine("        \"Name\": \"Console\",");
        sb.AppendLine("        \"Args\": {");
        sb.AppendLine("          \"outputTemplate\": \"[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}\"");
        sb.AppendLine("        }");
        sb.AppendLine("      },");
        sb.AppendLine("      {");
        sb.AppendLine("        \"Name\": \"File\",");
        sb.AppendLine("        \"Args\": {");
        sb.AppendLine("          \"path\": \"logs/ones_session_cleaner_.log\",");
        sb.AppendLine("          \"rollingInterval\": \"Day\",");
        sb.AppendLine("          \"retainedFileCountLimit\": 30,");
        sb.AppendLine("          \"outputTemplate\": \"{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}\"");
        sb.AppendLine("        }");
        sb.AppendLine("      }");
        sb.AppendLine("    ]");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private void MenuTerminate_Click(object sender, RoutedEventArgs e) => BtnTerminateSelected_Click(sender, e);
    private void MenuCopyUser_Click(object sender, RoutedEventArgs e)
    {
        if (GridSessions.SelectedItem is V8SessionInfo s && !string.IsNullOrEmpty(s.UserName))
            Clipboard.SetText(s.UserName);
    }
    private void MenuCopyBase_Click(object sender, RoutedEventArgs e)
    {
        if (GridSessions.SelectedItem is V8SessionInfo s && !string.IsNullOrEmpty(s.InfoBaseName))
            Clipboard.SetText(s.InfoBaseName);
    }
    private void MenuCopyId_Click(object sender, RoutedEventArgs e)
    {
        if (GridSessions.SelectedItem is V8SessionInfo s)
            Clipboard.SetText(s.SessionId.ToString());
    }
    private void HandleRacLogMessage(string msg)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (TxtRacLogConsole != null)
            {
                TxtRacLogConsole.AppendText(msg + Environment.NewLine);
                TxtRacLogConsole.ScrollToEnd();
            }
        });
    }

    private void BtnClearRacLog_Click(object sender, RoutedEventArgs e)
    {
        if (TxtRacLogConsole != null) TxtRacLogConsole.Clear();
    }

    private void BtnCopyRacLog_Click(object sender, RoutedEventArgs e)
    {
        if (TxtRacLogConsole != null && !string.IsNullOrEmpty(TxtRacLogConsole.Text))
        {
            Clipboard.SetText(TxtRacLogConsole.Text);
            MessageBox.Show(this, "Журнал диагностики RAC скопирован в буфер обмена.", "Скопировано", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
    private void GridSessions_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            GridSessions.UnselectAll();
            e.Handled = true;
        }
    }

    private void GridSessions_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is ScrollViewer || e.OriginalSource is Border b && b.Name == "DG_ScrollViewer")
        {
            GridSessions.UnselectAll();
        }
    }

    private void BtnClearSelection_Click(object sender, RoutedEventArgs e)
    {
        GridSessions.UnselectAll();
    }

    private async void BtnTestRac_Click(object sender, RoutedEventArgs e)
    {
        TxtStatusMessage.Text = "Выполняется диагностический тест RAC...";
        DotStatus.Fill = new SolidColorBrush(Color.FromRgb(0xF9, 0x73, 0x16));

        try
        {
            var executor = App.ServiceProvider.GetRequiredService<RacProcessExecutor>();
            var endpoints = _options.GetEndpoints();
            foreach (var ep in endpoints)
            {
                RacProcessExecutor.Log($"=== ДИАГНОСТИЧЕСКИЙ ТЕСТ ДЛЯ СЕРВЕРА: {ep.DisplayAddress} ===");
                try
                {
                    string res = await executor.ExecuteRacAsync($"cluster list {ep.Host}:{ep.RasPort}", ep.RacPath);
                    RacProcessExecutor.Log($"[РЕЗУЛЬТАТ CLUSTER LIST]:\n{res}");
                }
                catch (Exception ex)
                {
                    RacProcessExecutor.Log($"[ОШИБКА ДИАГНОСТИКИ]: {ex.Message}");
                }
            }
            TxtStatusMessage.Text = "Диагностический тест RAC завершен. Смотрите журнал.";
            DotStatus.Fill = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
        }
        catch (Exception ex)
        {
            TxtStatusMessage.Text = $"Ошибка теста: {ex.Message}";
            DotStatus.Fill = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
        }
    }
    private void SetSettingsDirty(bool dirty)
    {
        _isSettingsDirty = dirty;
        if (BtnSaveSettings != null)
        {
            BtnSaveSettings.IsEnabled = dirty;
            BtnSaveSettings.Style = (Style)FindResource(dirty ? "ShadcnOrangeButton" : "ShadcnSecondaryButton");
        }
    }

    private void MarkSettingsDirty() => SetSettingsDirty(true);

    private void HookUpSettingsDirtyTracking()
    {
        CfgServer.TextChanged += (_, _) => MarkSettingsDirty();
        CfgClusterAdminUser.TextChanged += (_, _) => MarkSettingsDirty();
        CfgClusterAdminPassword.PasswordChanged += (_, _) => MarkSettingsDirty();
        CfgRacPath.TextChanged += (_, _) => MarkSettingsDirty();
        CfgSlkServer.TextChanged += (_, _) => MarkSettingsDirty();
        CfgSlkUser.TextChanged += (_, _) => MarkSettingsDirty();
        CfgSlkPassword.PasswordChanged += (_, _) => MarkSettingsDirty();
        CfgOnlyHibernate.Checked += (_, _) => MarkSettingsDirty();
        CfgOnlyHibernate.Unchecked += (_, _) => MarkSettingsDirty();
        CfgMinMinutes.TextChanged += (_, _) => MarkSettingsDirty();
        CfgCleanFrozen.Checked += (_, _) => MarkSettingsDirty();
        CfgCleanFrozen.Unchecked += (_, _) => MarkSettingsDirty();
        CfgMaxDbProcMinutes.TextChanged += (_, _) => MarkSettingsDirty();
        CfgMaxCallDurationMinutes.TextChanged += (_, _) => MarkSettingsDirty();
        CfgExcludedUsers.TextChanged += (_, _) => MarkSettingsDirty();
        CfgExcludedBases.TextChanged += (_, _) => MarkSettingsDirty();
        CfgExcludedAppIds.TextChanged += (_, _) => MarkSettingsDirty();
    }

    private void BtnBrowseRacPath_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Утилита 1C RAC (rac.exe)|rac.exe|Все исполняемые файлы (*.exe)|*.exe",
            Title = "Выберите утилиту rac.exe"
        };
        if (dlg.ShowDialog() == true)
        {
            CfgRacPath.Text = dlg.FileName;
            MarkSettingsDirty();
        }
    }

    private static string GetAppBuildTimestamp()
    {
        try
        {
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                exePath = typeof(MainWindow).Assembly.Location;
            }

            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                return $" • {File.GetLastWriteTime(exePath):dd.MM.yyyy}";
            }
        }
        catch { }

        return $" • {DateTime.Now:dd.MM.yyyy}";
    }
}