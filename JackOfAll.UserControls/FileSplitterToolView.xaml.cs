using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using JackOfAll.Lib.Tools;
using Microsoft.Win32;

namespace JackOfAll.UserControls;

public partial class FileSplitterToolView : UserControl, IStatefulTool
{
    public FileSplitterToolView()
    {
        InitializeComponent();
    }

    private void OnBrowseInputClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Supported files|*.csv;*.txt;*.tsv;*.xlsx;*.xlsm|CSV files (*.csv)|*.csv|Text files (*.txt;*.tsv)|*.txt;*.tsv|Excel OpenXML (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dlg.ShowDialog() == true)
        {
            InputPathBox.Text = dlg.FileName;
            if (string.IsNullOrWhiteSpace(OutputFolderBox.Text))
                OutputFolderBox.Text = Path.Combine(Path.GetDirectoryName(dlg.FileName) ?? string.Empty, Path.GetFileNameWithoutExtension(dlg.FileName) + "_chunks");
        }
    }

    private void OnBrowseOutputClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Select output folder"
        };

        if (dlg.ShowDialog() == true)
            OutputFolderBox.Text = dlg.FolderName;
    }

    private void OnSplitClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var input = InputPathBox.Text.Trim();
            var outputFolder = OutputFolderBox.Text.Trim();
            if (!File.Exists(input))
            {
                SetStatus("Source file was not found.");
                return;
            }

            if (string.IsNullOrWhiteSpace(outputFolder))
            {
                SetStatus("Choose an output folder.");
                return;
            }

            Directory.CreateDirectory(outputFolder);
            var extension = Path.GetExtension(input).ToLowerInvariant();
            var isOpenXmlWorkbook = extension is ".xlsx" or ".xlsm";
            var isLegacyWorkbook = extension == ".xls";
            if (isLegacyWorkbook)
            {
                SetStatus("Legacy .xls files are binary workbooks. Save as .xlsx or CSV first, then split.");
                return;
            }

            if (isOpenXmlWorkbook && SplitBySize.IsChecked == true)
            {
                SetStatus("Excel workbooks cannot be byte-split into valid standalone workbooks. Use lines mode to export worksheet chunks as CSV.");
                return;
            }

            var created = isOpenXmlWorkbook
                ? SplitXlsxToCsvChunks(input, outputFolder)
                : SplitByLines.IsChecked == true
                    ? SplitLines(input, outputFolder)
                    : SplitBytes(input, outputFolder);

            SetStatus($"Created {created} chunk file(s) in {outputFolder}");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }


    private int SplitXlsxToCsvChunks(string input, string outputFolder)
    {
        if (!int.TryParse(LinesPerChunkBox.Text.Trim(), out var linesPerChunk) || linesPerChunk < 1)
            throw new InvalidOperationException("Lines per chunk must be a positive number.");

        using var archive = ZipFile.OpenRead(input);
        var sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml")
            ?? throw new InvalidOperationException("Could not find the first worksheet in this workbook.");

        var sharedStrings = ReadSharedStrings(archive);
        var suffix = GetSuffix();
        var baseName = Path.GetFileNameWithoutExtension(input);
        var repeatHeader = RepeatHeader.IsChecked == true;
        List<string>? header = null;
        StreamWriter? writer = null;
        var chunkIndex = 0;
        var lineInChunk = 0;
        var created = 0;

        try
        {
            using var sheetStream = sheetEntry.Open();
            using var reader = System.Xml.XmlReader.Create(sheetStream, new System.Xml.XmlReaderSettings { IgnoreWhitespace = true });
            while (reader.Read())
            {
                if (reader.NodeType != System.Xml.XmlNodeType.Element || reader.LocalName != "row")
                    continue;

                var rowElement = (XElement)XNode.ReadFrom(reader);
                var row = ReadXlsxRow(rowElement, sharedStrings);
                if (row.Count == 0)
                    continue;

                if (repeatHeader && header is null)
                {
                    header = row;
                    continue;
                }

                if (writer is null || lineInChunk >= linesPerChunk)
                {
                    writer?.Dispose();
                    chunkIndex++;
                    created++;
                    lineInChunk = 0;
                    writer = new StreamWriter(MakeChunkPath(outputFolder, baseName, suffix, chunkIndex, ".csv"));
                    if (header is not null)
                        writer.WriteLine(ToCsvLine(header));
                }

                writer.WriteLine(ToCsvLine(row));
                lineInChunk++;
            }

            if (created == 0 && header is not null)
            {
                created = 1;
                writer = new StreamWriter(MakeChunkPath(outputFolder, baseName, suffix, 1, ".csv"));
                writer.WriteLine(ToCsvLine(header));
            }
        }
        finally
        {
            writer?.Dispose();
        }

        return created;
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
            return [];

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        return document.Descendants()
            .Where(element => element.Name.LocalName == "si")
            .Select(item => string.Concat(item.Descendants().Where(element => element.Name.LocalName == "t").Select(element => element.Value)))
            .ToList();
    }

    private static List<string> ReadXlsxRow(XElement rowElement, List<string> sharedStrings)
    {
        var values = new SortedDictionary<int, string>();
        foreach (var cell in rowElement.Elements().Where(element => element.Name.LocalName == "c"))
        {
            var reference = cell.Attribute("r")?.Value ?? string.Empty;
            var columnIndex = GetColumnIndex(reference);
            if (columnIndex < 1)
                columnIndex = values.Count + 1;

            values[columnIndex] = ReadXlsxCell(cell, sharedStrings);
        }

        if (values.Count == 0)
            return [];

        var lastColumn = values.Keys.Max();
        var row = new List<string>(lastColumn);
        for (var i = 1; i <= lastColumn; i++)
            row.Add(values.TryGetValue(i, out var value) ? value : string.Empty);
        return row;
    }

    private static string ReadXlsxCell(XElement cell, List<string> sharedStrings)
    {
        var type = cell.Attribute("t")?.Value;
        if (type == "inlineStr")
            return string.Concat(cell.Descendants().Where(element => element.Name.LocalName == "t").Select(element => element.Value));

        var raw = cell.Elements().FirstOrDefault(element => element.Name.LocalName == "v")?.Value ?? string.Empty;
        if (type == "s" && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sharedIndex))
            return sharedIndex >= 0 && sharedIndex < sharedStrings.Count ? sharedStrings[sharedIndex] : string.Empty;

        if (type == "b")
            return raw == "1" ? "TRUE" : "FALSE";

        return raw;
    }

    private static int GetColumnIndex(string cellReference)
    {
        var index = 0;
        foreach (var ch in cellReference)
        {
            if (!char.IsLetter(ch))
                break;
            index = index * 26 + char.ToUpperInvariant(ch) - 'A' + 1;
        }

        return index;
    }

    private static string ToCsvLine(IReadOnlyList<string> values)
        => string.Join(',', values.Select(EscapeCsv));

    private static string EscapeCsv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\r') && !value.Contains('\n'))
            return value;

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
    private int SplitLines(string input, string outputFolder)
    {
        if (!int.TryParse(LinesPerChunkBox.Text.Trim(), out var linesPerChunk) || linesPerChunk < 1)
            throw new InvalidOperationException("Lines per chunk must be a positive number.");

        var suffix = GetSuffix();
        var extension = Path.GetExtension(input);
        var baseName = Path.GetFileNameWithoutExtension(input);
        var repeatHeader = RepeatHeader.IsChecked == true;
        string? header = null;
        StreamWriter? writer = null;
        var chunkIndex = 0;
        var lineInChunk = 0;
        var created = 0;

        try
        {
            using var reader = new StreamReader(input, detectEncodingFromByteOrderMarks: true);
            if (repeatHeader && !reader.EndOfStream)
                header = reader.ReadLine();

            while (!reader.EndOfStream)
            {
                if (writer is null || lineInChunk >= linesPerChunk)
                {
                    writer?.Dispose();
                    chunkIndex++;
                    created++;
                    lineInChunk = 0;
                    writer = new StreamWriter(MakeChunkPath(outputFolder, baseName, suffix, chunkIndex, extension));
                    if (header is not null)
                        writer.WriteLine(header);
                }

                writer.WriteLine(reader.ReadLine());
                lineInChunk++;
            }

            if (created == 0 && header is not null)
            {
                chunkIndex++;
                created++;
                writer = new StreamWriter(MakeChunkPath(outputFolder, baseName, suffix, chunkIndex, extension));
                writer.WriteLine(header);
            }
        }
        finally
        {
            writer?.Dispose();
        }

        return created;
    }

    private int SplitBytes(string input, string outputFolder)
    {
        var chunkSize = GetChunkSizeBytes();
        var suffix = GetSuffix();
        var extension = Path.GetExtension(input);
        var baseName = Path.GetFileNameWithoutExtension(input);
        var buffer = new byte[1024 * 1024];
        var created = 0;

        using var source = File.OpenRead(input);
        while (source.Position < source.Length)
        {
            created++;
            var remainingInChunk = chunkSize;
            using var target = File.Create(MakeChunkPath(outputFolder, baseName, suffix, created, extension));
            while (remainingInChunk > 0)
            {
                var readSize = (int)Math.Min(buffer.Length, remainingInChunk);
                var read = source.Read(buffer, 0, readSize);
                if (read == 0)
                    break;
                target.Write(buffer, 0, read);
                remainingInChunk -= read;
            }
        }

        return created;
    }

    private long GetChunkSizeBytes()
    {
        if (!long.TryParse(SizeValueBox.Text.Trim(), out var size) || size < 1)
            throw new InvalidOperationException("Chunk size must be a positive number.");

        var unit = ((ComboBoxItem)SizeUnitBox.SelectedItem).Content?.ToString() ?? "MB";
        return unit switch
        {
            "KB" => size * 1024L,
            "GB" => size * 1024L * 1024L * 1024L,
            _ => size * 1024L * 1024L
        };
    }

    private string GetSuffix()
        => string.IsNullOrWhiteSpace(SuffixBox.Text) ? "part" : SuffixBox.Text.Trim();

    private static string MakeChunkPath(string folder, string baseName, string suffix, int index, string extension)
        => Path.Combine(folder, $"{baseName}.{suffix}-{index:000}{extension}");

    private void OnOpenOutputClick(object sender, RoutedEventArgs e)
    {
        var outputFolder = OutputFolderBox.Text.Trim();
        if (Directory.Exists(outputFolder))
            Process.Start(new ProcessStartInfo(outputFolder) { UseShellExecute = true });
    }

    private void SetStatus(string message) => StatusText.Text = message;

    public Dictionary<string, string> SaveState() => new()
    {
        ["input"] = InputPathBox.Text,
        ["output"] = OutputFolderBox.Text,
        ["lines"] = LinesPerChunkBox.Text,
        ["size"] = SizeValueBox.Text,
        ["unit"] = SizeUnitBox.SelectedIndex.ToString(),
        ["suffix"] = SuffixBox.Text,
        ["byLines"] = (SplitByLines.IsChecked == true).ToString(),
        ["repeatHeader"] = (RepeatHeader.IsChecked == true).ToString()
    };

    public void LoadState(Dictionary<string, string> state)
    {
        if (state.TryGetValue("input", out var input)) InputPathBox.Text = input;
        if (state.TryGetValue("output", out var output)) OutputFolderBox.Text = output;
        if (state.TryGetValue("lines", out var lines)) LinesPerChunkBox.Text = lines;
        if (state.TryGetValue("size", out var size)) SizeValueBox.Text = size;
        if (state.TryGetValue("unit", out var unit) && int.TryParse(unit, out var unitIndex) && unitIndex >= 0 && unitIndex < SizeUnitBox.Items.Count) SizeUnitBox.SelectedIndex = unitIndex;
        if (state.TryGetValue("suffix", out var suffix)) SuffixBox.Text = suffix;
        if (state.TryGetValue("byLines", out var byLines))
        {
            SplitByLines.IsChecked = byLines == "True";
            SplitBySize.IsChecked = byLines != "True";
        }
        if (state.TryGetValue("repeatHeader", out var repeatHeader)) RepeatHeader.IsChecked = repeatHeader == "True";
    }
}