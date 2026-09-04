using System.IO.Compression;
using System.Security;
using System.Text;
using System.Xml;
using OneSSessionMonitor.Core.Models;

namespace OneSSessionMonitor.Core.Services;

public static class ExcelExportService
{
    public static void ExportSessionsToExcel(string filePath, IReadOnlyList<V8SessionInfo> sessions)
    {
        string[] headers = [
            "Статус", "ID Сеанса", "Пользователь", "Информационная база",
            "Лицензии", "ОЗУ (RAM)", "ЦП (CPU)", "Время неактивности",
            "Клиентское приложение", "Сервер 1С", "Кластер", "Хост клиента", "Время начала"
        ];

        List<string[]> rows = new(sessions.Count);
        foreach (var s in sessions)
        {
            rows.Add([
                s.StatusText,
                s.SessionId.ToString(),
                s.UserName,
                s.InfoBaseName,
                s.FormattedLicenses,
                s.FormattedMemory,
                s.FormattedCpuTime,
                s.FormattedHibernateDuration,
                s.AppId,
                s.Server,
                s.ClusterName,
                s.Host ?? "",
                s.FormattedStartedAt
            ]);
        }

        GenerateXlsx(filePath, "Сеансы 1С", headers, rows);
    }

    public static void GenerateXlsx(string filePath, string sheetName, string[] headers, IReadOnlyList<string[]> rows)
    {
        var tempFile = filePath + ".tmp";
        if (File.Exists(tempFile)) File.Delete(tempFile);

        using (var zipStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, false))
        {
            CreateEntry(archive, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/></Types>");
            CreateEntry(archive, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            CreateEntry(archive, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
            var safeSheetName = SecurityElement.Escape(sheetName) ?? "Sheet1";
            CreateEntry(archive, "xl/workbook.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"{safeSheetName}\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            CreateEntry(archive, "xl/styles.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"2\"><font><sz val=\"10\"/><name val=\"Segoe UI\"/></font><font><b/><sz val=\"10.5\"/><color rgb=\"FFFFFFFF\"/><name val=\"Segoe UI\"/></font></fonts><fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFEA580C\"/></patternFill></fill></fills><borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border><border><left style=\"thin\"><color rgb=\"FFCBD5E1\"/></left><right style=\"thin\"><color rgb=\"FFCBD5E1\"/></right><top style=\"thin\"><color rgb=\"FFCBD5E1\"/></top><bottom style=\"thin\"><color rgb=\"FFCBD5E1\"/></bottom></border></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\"/><xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf></cellXfs></styleSheet>");

            var sheetEntry = archive.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Fastest);
            using (var sheetStream = sheetEntry.Open())
            using (var writer = XmlWriter.Create(sheetStream, new XmlWriterSettings { Encoding = Encoding.UTF8, Indent = false }))
            {
                writer.WriteStartDocument(true);
                writer.WriteStartElement("worksheet", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
                writer.WriteAttributeString("xmlns", "r", null, "http://schemas.openxmlformats.org/officeDocument/2006/relationships");

                writer.WriteStartElement("cols");
                for (int colIdx = 0; colIdx < headers.Length; colIdx++)
                {
                    writer.WriteStartElement("col");
                    writer.WriteAttributeString("min", (colIdx + 1).ToString());
                    writer.WriteAttributeString("max", (colIdx + 1).ToString());
                    var width = colIdx switch
                    {
                        0 => "14",
                        1 => "12",
                        2 => "24",
                        3 => "26",
                        4 => "24",
                        5 => "14",
                        6 => "14",
                        7 => "20",
                        8 => "14",
                        9 => "18",
                        10 => "18",
                        11 => "18",
                        12 => "14",
                        _ => "18"
                    };
                    writer.WriteAttributeString("width", width);
                    writer.WriteAttributeString("customWidth", "1");
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();

                writer.WriteStartElement("sheetData");
                writer.WriteStartElement("row");
                writer.WriteAttributeString("r", "1");
                writer.WriteAttributeString("ht", "24");
                writer.WriteAttributeString("customHeight", "1");

                for (int colIdx = 0; colIdx < headers.Length; colIdx++)
                {
                    var cellRef = GetCellRef(colIdx, 1);
                    writer.WriteStartElement("c");
                    writer.WriteAttributeString("r", cellRef);
                    writer.WriteAttributeString("t", "inlineStr");
                    writer.WriteAttributeString("s", "1");
                    writer.WriteStartElement("is");
                    writer.WriteElementString("t", headers[colIdx]);
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();

                for (int rowIdx = 0; rowIdx < rows.Count; rowIdx++)
                {
                    int excelRowNum = rowIdx + 2;
                    var rowData = rows[rowIdx];
                    writer.WriteStartElement("row");
                    writer.WriteAttributeString("r", excelRowNum.ToString());
                    for (int colIdx = 0; colIdx < rowData.Length; colIdx++)
                    {
                        var cellRef = GetCellRef(colIdx, excelRowNum);
                        var val = rowData[colIdx] ?? string.Empty;
                        writer.WriteStartElement("c");
                        writer.WriteAttributeString("r", cellRef);
                        writer.WriteAttributeString("t", "inlineStr");
                        writer.WriteAttributeString("s", "0");
                        writer.WriteStartElement("is");
                        writer.WriteElementString("t", SanitizeXml(val));
                        writer.WriteEndElement();
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
                writer.WriteEndElement();
                writer.WriteEndDocument();
            }
        }

        if (File.Exists(filePath)) File.Delete(filePath);
        File.Move(tempFile, filePath);
    }

    private static void CreateEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        writer.Write(content.Trim());
    }

    private static string GetCellRef(int colIndex, int rowIndex)
    {
        var colName = "";
        int dividend = colIndex + 1;
        while (dividend > 0)
        {
            int modulo = (dividend - 1) % 26;
            colName = Convert.ToChar(65 + modulo) + colName;
            dividend = (dividend - modulo) / 26;
        }
        return $"{colName}{rowIndex}";
    }

    private static string SanitizeXml(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            if (XmlConvert.IsXmlChar(c))
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
