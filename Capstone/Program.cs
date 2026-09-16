using Microsoft.VisualBasic;
using Capstone;
using Octokit;

var client = new GitHubClient(new  ProductHeaderValue("godotengine"));
Env.AddAccessToken(client);


long startTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();

var r = await client.Issue.GetAllForRepository("godotengine", "godot");
Console.WriteLine($"{r.Count} issues found");

long endTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();

Console.WriteLine($"got all issues in {endTime - startTime}ms");