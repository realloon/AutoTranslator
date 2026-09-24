namespace Translator.Helpers;

internal static class ModPathHelper {
    public static bool IsPathUnderRoot(string path, string root) {
        return Normalize(path).StartsWith(Normalize(root), StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalize(string path) {
        return Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
    }
}