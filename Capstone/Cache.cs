using Octokit.Internal;
namespace Capstone;

public static class Cache
{
    private static string GetCacheDirectory()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !directory.GetFiles("*.sln").Any())
        {
            directory = directory.Parent;
        }

        if (directory == null)
        {
            throw new DirectoryNotFoundException("solution directory was not found");
        }

        var cacheDir = directory + "\\.cache\\";
        Directory.CreateDirectory(cacheDir);
        return cacheDir;
    }

    private static readonly string CacheDirectory = GetCacheDirectory();
    private static readonly SimpleJsonSerializer Serializer = new SimpleJsonSerializer();
    

    private static T? GetCache<T>(string name)
    {
        try
        {
            // if i knew C# path utilities this would be nicer
            string path = CacheDirectory + name + ".json";
            return Serializer.Deserialize<T>(File.ReadAllText(path));
        }
        catch
        {
            Console.WriteLine($"{name} was not found");
            return default;
        }
    }


    private static void SetCache<T>(string name, T value)
    {
        try
        {
            // if i knew C# path utilities this would be nicer
            string path = CacheDirectory + name + ".json";
            File.WriteAllText(path, Serializer.Serialize(value));
        }
        catch (Exception e)
        {
            Console.WriteLine($"error in caching {name}]]\nThe following exception was skipped:\n{e.ToString()}");
        }
    }


    public static async Task<T> GetIfCached<T>(Func<Task<T>> callback, string cacheName, bool forceCacheReload = false)
    {
        var cache = GetCache<T>(cacheName);
        if (cache != null && !forceCacheReload)
        {
            return cache;
        }

        var data = await callback();
        SetCache(cacheName, data);
        return data;
    }

}