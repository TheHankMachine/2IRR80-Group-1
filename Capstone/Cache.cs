using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using Octokit.Internal;

namespace Capstone;
using Octokit;

public class Cache
{
    private static DirectoryInfo GetSolutionDirectory()
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

        return directory;
    }

    private static readonly string CacheDirectory = GetSolutionDirectory() + "\\.cache\\";
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
        catch
        {
            Console.WriteLine($"error in caching {name}");
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
        SetCache("issues", data);
        return data;
    }

}