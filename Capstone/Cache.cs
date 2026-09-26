using System.Text.RegularExpressions;
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
    

    private static T GetCache<T>(string name)
    {
        // try
        // {
        string path = CacheDirectory + name + ".json";
        return Serializer.Deserialize<T>(File.ReadAllText(path));
        // }
        // catch
        // {
        //     return null;
        // }
    }


    private static string GetFileSafeKey(string key)
    {
        return Regex.Replace(key, @"[\/:*?<>|]", "");
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


    public static async Task<T> GetIfCached<T>(Func<Task<T>> callback, string key, bool forceCacheReload = false)
    {
        var cacheName = GetFileSafeKey(key);
        try
        {
            var cache = GetCache<T>(cacheName);
            if (cache != null && !forceCacheReload)
            {
                return cache;
            }
            // sorry
            throw new Exception("goto catch block");
        }
        catch (Exception e)
        {
            var data = await callback();
            SetCache(cacheName, data);
            return data;
        }
    }

}