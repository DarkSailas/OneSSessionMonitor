using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace OneSSessionMonitor.Core.Ras;

public sealed class RacProcessExecutor(ILogger<RacProcessExecutor>? logger = null)
{
    public static event Action<string>? OnRacLog;
    public static void Log(string msg) => OnRacLog?.Invoke(msg);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetErrorMode(uint uMode);

    private const uint SEM_FAILCRITICALERRORS = 0x0001;
    private const uint SEM_NOGPFAULTERRORBOX = 0x0002;

    static RacProcessExecutor()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX);
            }
            catch { }
        }
    }

    /// <summary>
    /// Проверяет наличие ключевых библиотек ядра 1С (core85.dll, core83.dll и др.) в каталоге утилиты.
    /// </summary>
    public static bool Has1CCoreRuntime(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return false;

        // Явная поддержка 1С:Предприятие 8.5 и 8.3
        if (File.Exists(Path.Combine(dir, "core85.dll")) ||
            File.Exists(Path.Combine(dir, "core83.dll")) ||
            File.Exists(Path.Combine(dir, "core84.dll")) ||
            File.Exists(Path.Combine(dir, "core82.dll")))
        {
            return true;
        }

        try
        {
            var opt = new EnumerationOptions
            {
                MatchCasing = MatchCasing.CaseInsensitive,
                RecurseSubdirectories = false
            };
            return Directory.EnumerateFiles(dir, "core8*.dll", opt).Any();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Возвращает имя найденной библиотеки ядра (core85.dll, core83.dll и т.д.).
    /// </summary>
    public static string GetCoreDllDisplayName(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return "core85.dll / core83.dll";

        if (File.Exists(Path.Combine(dir, "core85.dll"))) return "core85.dll";
        if (File.Exists(Path.Combine(dir, "core83.dll"))) return "core83.dll";
        if (File.Exists(Path.Combine(dir, "core84.dll"))) return "core84.dll";
        if (File.Exists(Path.Combine(dir, "core82.dll"))) return "core82.dll";

        try
        {
            var opt = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive };
            var file = Directory.EnumerateFiles(dir, "core8*.dll", opt).FirstOrDefault();
            if (file != null) return Path.GetFileName(file);
        }
        catch { }

        return "core85.dll / core83.dll";
    }

    /// <summary>
    /// Извлекает числовой номер версии платформы 1С из пути или метаданных файла для корректной сортировки.
    /// </summary>
    public static Version ExtractVersionFromPath(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var fvi = FileVersionInfo.GetVersionInfo(path);
                if (!string.IsNullOrWhiteSpace(fvi.FileVersion))
                {
                    string clean = fvi.FileVersion.Replace(',', '.').Trim();
                    var match = Regex.Match(clean, @"\b(\d+(?:\.\d+)+)\b");
                    if (match.Success && Version.TryParse(match.Groups[1].Value, out var fv))
                    {
                        return fv;
                    }
                }
            }
        }
        catch { }

        try
        {
            var match = Regex.Match(path, @"\b(\d+\.\d+\.\d+(?:\.\d+)?)\b");
            if (match.Success && Version.TryParse(match.Groups[1].Value, out var v))
            {
                return v;
            }
        }
        catch { }

        return new Version(0, 0);
    }

    /// <summary>
    /// Находит все исполняемые файлы rac в стандартных каталогах установки 1С.
    /// </summary>
    public static List<string> FindAllInstalledRacPaths()
    {
        var found = new List<string>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string[] searchRoots = [
                @"C:\Program Files\1cv8",
                @"C:\Program Files (x86)\1cv8"
            ];

            foreach (var root in searchRoots)
            {
                if (!Directory.Exists(root)) continue;

                // 1. Прямой быстрый опрос версионных папок первого уровня (8.3.xx.yyyy\bin\rac.exe)
                try
                {
                    foreach (var verDir in Directory.GetDirectories(root))
                    {
                        var racInBin = Path.Combine(verDir, "bin", "rac.exe");
                        if (File.Exists(racInBin) && !found.Contains(racInBin, StringComparer.OrdinalIgnoreCase))
                        {
                            found.Add(racInBin);
                        }

                        var racDirect = Path.Combine(verDir, "rac.exe");
                        if (File.Exists(racDirect) && !found.Contains(racDirect, StringComparer.OrdinalIgnoreCase))
                        {
                            found.Add(racDirect);
                        }
                    }
                }
                catch { }

                // 2. Безопасный рекурсивный поиск с пропуском недоступных каталогов
                try
                {
                    var opt = new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        MatchCasing = MatchCasing.CaseInsensitive,
                        MaxRecursionDepth = 6
                    };

                    foreach (var f in Directory.EnumerateFiles(root, "rac.exe", opt))
                    {
                        if (!found.Contains(f, StringComparer.OrdinalIgnoreCase))
                        {
                            found.Add(f);
                        }
                    }
                }
                catch { }
            }
        }
        else
        {
            string[] linuxPaths = [
                "/opt/1cv8/current/rac",
                "/opt/1C/v8.3/x86_64/rac",
                "/opt/1C/v8.3/i386/rac",
                "/usr/bin/rac"
            ];
            foreach (var p in linuxPaths)
            {
                if (File.Exists(p) && !found.Contains(p, StringComparer.OrdinalIgnoreCase))
                {
                    found.Add(p);
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Находит каталоги, содержащие библиотеки ядра 1С (core83.dll).
    /// </summary>
    public static List<string> FindAllCoreDllDirectories()
    {
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string[] searchRoots = [
                @"C:\Program Files\1cv8",
                @"C:\Program Files (x86)\1cv8"
            ];

            foreach (var root in searchRoots)
            {
                if (!Directory.Exists(root)) continue;

                try
                {
                    foreach (var verDir in Directory.GetDirectories(root))
                    {
                        var bin = Path.Combine(verDir, "bin");
                        if (Has1CCoreRuntime(bin))
                        {
                            dirs.Add(bin);
                        }
                    }
                }
                catch { }
            }
        }

        return dirs.ToList();
    }

    /// <summary>
    /// Ищет самую свежую рабочую утилиту rac.exe, проверяя наличие библиотеки core83.dll.
    /// </summary>
    public static string? FindBestWorkingRac()
    {
        var all = FindAllInstalledRacPaths();
        if (all.Count == 0) return null;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Отбираем только те утилиты rac.exe, рядом с которыми есть core83.dll / core*.dll
            var valid = all
                .Where(p => Has1CCoreRuntime(Path.GetDirectoryName(p)))
                .OrderByDescending(ExtractVersionFromPath)
                .ToList();

            if (valid.Count > 0)
            {
                return valid[0];
            }
        }
        else
        {
            return all[0];
        }

        return null;
    }

    public string ResolveRacExecutablePath(string? customPath = null)
    {
        // 1. Если задан пользовательский путь к rac.exe
        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var customDir = Path.GetDirectoryName(customPath);
                if (Has1CCoreRuntime(customDir))
                {
                    return customPath;
                }

                OnRacLog?.Invoke($"[{DateTime.Now:HH:mm:ss.fff}] [WARN] В каталоге указанного rac.exe ('{customPath}') не найдена библиотека ядра 1С (core85.dll / core83.dll)! Поиск исправной версии 1С...");
                var fallback = FindBestWorkingRac();
                if (!string.IsNullOrEmpty(fallback))
                {
                    var coreName = GetCoreDllDisplayName(Path.GetDirectoryName(fallback));
                    OnRacLog?.Invoke($"[{DateTime.Now:HH:mm:ss.fff}] [INFO] Выбран исправный rac.exe: \"{fallback}\" (ядро: {coreName})");
                    return fallback;
                }
            }

            return customPath;
        }

        // 2. Автоматический поиск самой свежей рабочей версии с core85.dll / core83.dll
        var best = FindBestWorkingRac();
        if (!string.IsNullOrEmpty(best))
        {
            var v = ExtractVersionFromPath(best);
            var coreName = GetCoreDllDisplayName(Path.GetDirectoryName(best));
            OnRacLog?.Invoke($"[{DateTime.Now:HH:mm:ss.fff}] Автоматически выбран рабочий rac.exe: \"{best}\" (v{v}, ядро: {coreName})");
            return best;
        }

        // 3. Диагностический лог всех найденных утилит rac.exe
        var all = FindAllInstalledRacPaths();
        if (all.Count > 0)
        {
            OnRacLog?.Invoke($"[{DateTime.Now:HH:mm:ss.fff}] [WARN] Найдено {all.Count} экземпляров rac.exe, но ни один не содержит рядом core85.dll / core83.dll:");
            foreach (var rac in all)
            {
                var dir = Path.GetDirectoryName(rac);
                bool hasCore = Has1CCoreRuntime(dir);
                var coreName = GetCoreDllDisplayName(dir);
                var v = ExtractVersionFromPath(rac);
                OnRacLog?.Invoke($"[{DateTime.Now:HH:mm:ss.fff}]   • \"{rac}\" (v{v}, ядро: {(hasCore ? coreName : "НЕТ")})");
            }

            var coreDirs = FindAllCoreDllDirectories();
            if (coreDirs.Count > 0)
            {
                OnRacLog?.Invoke($"[{DateTime.Now:HH:mm:ss.fff}] [INFO] Каталоги с библиотеками ядра 1С без rac.exe:");
                foreach (var cd in coreDirs)
                {
                    OnRacLog?.Invoke($"[{DateTime.Now:HH:mm:ss.fff}]   • \"{cd}\"");
                }
            }

            all.Sort((a, b) => ExtractVersionFromPath(a).CompareTo(ExtractVersionFromPath(b)));
            return all[^1];
        }

        return "rac";
    }

    public async ValueTask<string> ExecuteRacAsync(string arguments, string? customRacPath = null, CancellationToken cancellationToken = default)
    {
        string exe = ResolveRacExecutablePath(customRacPath);
        string? exeDir = Path.GetDirectoryName(exe);

        // Дополнительная валидация перед запуском на Windows
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !string.IsNullOrEmpty(exeDir) && Directory.Exists(exeDir))
        {
            if (!Has1CCoreRuntime(exeDir))
            {
                var alternative = FindBestWorkingRac();
                if (!string.IsNullOrEmpty(alternative) && !string.Equals(alternative, exe, StringComparison.OrdinalIgnoreCase))
                {
                    var altCore = GetCoreDllDisplayName(Path.GetDirectoryName(alternative));
                    OnRacLog?.Invoke($"[{DateTime.Now:HH:mm:ss.fff}] [WARN] '{exe}' не содержит библиотеку ядра. Переключаемся на исправную платформу: \"{alternative}\" (ядро: {altCore})");
                    exe = alternative;
                    exeDir = Path.GetDirectoryName(exe);
                }
                else
                {
                    string missingMsg = $"В каталоге '{exeDir}' отсутствует библиотека ядра платформы 1С (core85.dll / core83.dll).\nУтилита rac.exe не может быть выполнена.\nУкажите путь к rac.exe из установленного Сервера 1С в настройках программы или восстановите компоненты платформы 1С.";
                    OnRacLog?.Invoke($"[{DateTime.Now:HH:mm:ss.fff}] [FATAL] {missingMsg}");
                    logger?.LogError("В каталоге '{ExeDir}' отсутствует core85.dll / core83.dll", exeDir);
                    throw new InvalidOperationException(missingMsg);
                }
            }
        }

        string logPrefix = $"[{DateTime.Now:HH:mm:ss.fff}] RAC: \"{exe}\" {arguments}";
        OnRacLog?.Invoke(logPrefix);

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            ErrorDialog = false
        };

        if (!string.IsNullOrEmpty(exeDir) && Directory.Exists(exeDir))
        {
            psi.WorkingDirectory = exeDir;

            var currentPath = psi.EnvironmentVariables.ContainsKey("PATH")
                ? psi.EnvironmentVariables["PATH"]
                : Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

            if (!string.IsNullOrEmpty(currentPath) && !currentPath.Contains(exeDir, StringComparison.OrdinalIgnoreCase))
            {
                psi.EnvironmentVariables["PATH"] = $"{exeDir};{currentPath}";
            }
        }

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            string err = $"[{DateTime.Now:HH:mm:ss.fff}] [FATAL] Ошибка запуска RAC: {ex.Message}";
            OnRacLog?.Invoke(err);
            logger?.LogError(ex, "Не удалось запустить утилиту 1C RAC: {Exe}", exe);
            throw new InvalidOperationException($"Не удалось запустить rac по пути '{exe}'. Убедитесь, что 1С:Предприятие установлено или укажите корректный RacPath в настройках.", ex);
        }

        using var stdoutMs = new MemoryStream();
        using var stderrMs = new MemoryStream();

        var stdoutTask = process.StandardOutput.BaseStream.CopyToAsync(stdoutMs, cancellationToken);
        var stderrTask = process.StandardError.BaseStream.CopyToAsync(stderrMs, cancellationToken);

        await Task.WhenAll(stdoutTask, stderrTask);
        await process.WaitForExitAsync(cancellationToken);

        string stdout = DecodeProcessBytes(stdoutMs.ToArray());
        string stderr = DecodeProcessBytes(stderrMs.ToArray());

        if (process.ExitCode != 0)
        {
            if (process.ExitCode == -1073741515 || process.ExitCode == unchecked((int)0xC0000135))
            {
                string dllMissingMsg = $"RAC завершился с ошибкой STATUS_DLL_NOT_FOUND (0xC0000135): не обнаружена библиотека ядра платформы 1С (core85.dll / core83.dll).\nКаталог '{exeDir}' не содержит полных библиотек платформы 1С:Предприятие.\nУкажите корректный путь к rac.exe из каталога установленного Сервера 1С в настройках программы или восстановите компоненты платформы 1С.";
                OnRacLog?.Invoke($"[{DateTime.Now:HH:mm:ss.fff}] [ERROR Code {process.ExitCode}] {dllMissingMsg}");
                throw new InvalidOperationException(dllMissingMsg);
            }

            string errMsg = $"RAC завершился с кодом ошибки {process.ExitCode}:\nSTDERR: {stderr}\nSTDOUT: {stdout}";
            OnRacLog?.Invoke($"[{DateTime.Now:HH:mm:ss.fff}] [ERROR Code {process.ExitCode}] {stderr} {stdout}");
            throw new InvalidOperationException(errMsg);
        }

        OnRacLog?.Invoke($"[{DateTime.Now:HH:mm:ss.fff}] [OK ExitCode 0]");
        return stdout;
    }

    public static string DecodeProcessBytes(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return string.Empty;

        try
        {
            var utf8Strict = new UTF8Encoding(false, true);
            string text = utf8Strict.GetString(bytes);
            return text;
        }
        catch (DecoderFallbackException) { }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                var cp866 = Encoding.GetEncoding(866);
                return cp866.GetString(bytes);
            }
            catch { }

            try
            {
                var win1251 = Encoding.GetEncoding(1251);
                return win1251.GetString(bytes);
            }
            catch { }
        }

        return Encoding.Default.GetString(bytes);
    }
}