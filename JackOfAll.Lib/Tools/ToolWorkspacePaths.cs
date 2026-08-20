using System.IO;

namespace JackOfAll.Lib.Tools;

public static class ToolWorkspacePaths
{
    public const string CodeRunnerFolderName = "Code Runner";
    public const string ApiClientFolderName = "API Client";
    public const string EditorFolderName = "Editor";

    public static string GetCodeRunnerFolder(AppEnvironment environment) =>
        GetOrCreateFolder(environment, CodeRunnerFolderName);

    public static string GetApiClientFolder(AppEnvironment environment) =>
        GetOrCreateFolder(environment, ApiClientFolderName);

    public static string GetEditorFolder(AppEnvironment environment) =>
        GetOrCreateFolder(environment, EditorFolderName);

    private static string GetOrCreateFolder(AppEnvironment environment, string folderName)
    {
        var folder = Path.Combine(environment.FolderPath, folderName);
        Directory.CreateDirectory(folder);
        return folder;
    }
}
