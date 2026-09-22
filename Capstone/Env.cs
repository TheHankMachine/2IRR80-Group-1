using Octokit;

namespace Capstone;
using DotNetEnv;

public static class Env
{
    private static readonly string EnvPath = GetEnvPath();
    
    private static string GetEnvPath()
    {
        var directory = new DirectoryInfo( Directory.GetCurrentDirectory());
        while (
            directory != null 
            && !directory.GetFiles(".env").Any()
            && !directory.GetFiles("*.sln").Any()   // don't look further than solution
        )
        {
            directory = directory.Parent;
        }

        if (directory == null || !directory.GetFiles(".env").Any())
        {
            throw new DirectoryNotFoundException("solution directory was not found");
        }

        return directory.FullName + "\\.env";
    }

    public static void AddAccessToken(Octokit.GitHubClient client) 
    {
        
        DotNetEnv.Env.Load(EnvPath);
        string token = Environment.GetEnvironmentVariable("ACCESS_TOKEN")!;
        if (token == null)
        {
            throw new EnvVariableNotFoundException("ACCESS_TOKEN not found in .env (or .env not found idk)", "ACCESS_TOKEN");
        }
        client.Credentials = new Credentials(token);
    }
}