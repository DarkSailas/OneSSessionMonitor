using System.IO;
using System.IO.Compression;
using OneSSessionMonitor.Core.Update;

namespace OneSSessionMonitor.Gui;

/// <summary>
/// Установка обновления GUI поверх работающей программы: занятые файлы переименовываются в *.osm-update-old,
/// на их место копируются новые. Настройки пользователя не затрагиваются.
/// </summary>
internal static class GuiUpdateInstaller
{
    // Собственный суффикс: при очистке нельзя задеть чужие файлы *.old в каталоге программы
    private const string BackupSuffix = ".osm-update-old";

    private static readonly EnumerationOptions BackupEnumeration = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = true
    };

    private static readonly string[] PreservedFiles = ["appsettings.json", "gui_state.json"];

    public static async Task InstallAsync(
        GitHubUpdateClient client,
        UpdateInfo update,
        string appDirectory,
        string exeName,
        CancellationToken ct)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "OneSSessionMonitorUpdate", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            string zipPath = Path.Combine(tempDir, "update.zip");
            string extractDir = Path.Combine(tempDir, "content");

            await client.DownloadAsync(update, zipPath, ct);
            await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, extractDir), ct);

            string contentRoot = ResolveContentRoot(extractDir, exeName);
            ct.ThrowIfCancellationRequested();

            // С этого места отмена не проверяется: замена файлов либо проходит целиком, либо откатывается.
            await Task.Run(() => ReplaceFiles(contentRoot, appDirectory), CancellationToken.None);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    /// <summary>Удаляет резервные копии, оставшиеся после предыдущего обновления.</summary>
    public static void CleanupBackups(string appDirectory)
    {
        try
        {
            foreach (string backup in Directory.EnumerateFiles(appDirectory, "*" + BackupSuffix, BackupEnumeration))
            {
                try
                {
                    File.Delete(backup);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Файл ещё занят завершающимся процессом — удалится при следующем запуске.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Нет доступа к каталогу программы — чистить нечего.
        }
    }

    private static string ResolveContentRoot(string extractDir, string exeName)
    {
        if (File.Exists(Path.Combine(extractDir, exeName)))
            return extractDir;

        string[] directories = Directory.GetDirectories(extractDir);
        if (directories.Length == 1
            && Directory.GetFiles(extractDir).Length == 0
            && File.Exists(Path.Combine(directories[0], exeName)))
        {
            return directories[0];
        }

        throw new InvalidDataException($"В архиве обновления не найден файл {exeName}.");
    }

    private static void ReplaceFiles(string sourceRoot, string appDirectory)
    {
        var copied = new List<string>();
        var backups = new List<(string Target, string Backup)>();

        try
        {
            foreach (string source in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourceRoot, source);
                if (PreservedFiles.Contains(relative, StringComparer.OrdinalIgnoreCase))
                    continue;

                string target = Path.Combine(appDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                if (File.Exists(target))
                {
                    string backup = target + BackupSuffix;
                    File.Delete(backup);
                    File.Move(target, backup);
                    backups.Add((target, backup));
                }

                // В список до копирования: при сбое посреди записи недописанный файл тоже нужно убрать
                copied.Add(target);
                File.Copy(source, target);
            }
        }
        catch (Exception installError)
        {
            foreach (string file in copied)
                TryDeleteFile(file);

            var notRestored = new List<string>();
            foreach (var (target, backup) in backups)
            {
                try
                {
                    File.Move(backup, target, overwrite: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Остальные файлы всё равно возвращаем на место.
                    notRestored.Add(Path.GetFileName(target));
                }
            }

            if (notRestored.Count > 0)
            {
                throw new IOException(
                    $"Обновление прервано, часть файлов не удалось вернуть на место ({string.Join(", ", notRestored)}). " +
                    $"Прежние версии лежат рядом с суффиксом {BackupSuffix}. Причина: {installError.Message}", installError);
            }

            throw;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
