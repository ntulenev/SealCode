using System.Reflection;
using System.Text.RegularExpressions;

using FluentAssertions;

using Microsoft.AspNetCore.SignalR;

using Transport;

namespace SealCode.Tests;

public sealed partial class HubContractTests
{
    [Fact]
    public void BrowserAndServerContractsShouldMatch()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SealCode.slnx")))
        {
            directory = directory.Parent;
        }
        directory.Should().NotBeNull();
        var source = File.ReadAllText(Path.Combine(directory!.FullName, "SealCode", "wwwroot", "hub-contract.js"));
        foreach (var type in new[] { typeof(HubContract.Methods), typeof(HubContract.Events) })
        {
            var block = Regex.Match(source, $@"Hub{type.Name} = Object\.freeze\(\{{(?<body>[\s\S]*?)\}}\)", RegexOptions.CultureInvariant).Groups["body"].Value;
            var browser = ContractPairs().Matches(block)
                .ToDictionary(match => match.Groups["key"].Value, match => match.Groups["value"].Value);
            var server = type.GetFields(BindingFlags.Public | BindingFlags.Static)
                .ToDictionary(field => field.Name, field => (string)field.GetRawConstantValue()!);
            browser.Should().BeEquivalentTo(server);
        }
        var names = typeof(HubContract.Methods).GetFields().Select(field => (string)field.GetRawConstantValue()!).ToArray();
        foreach (var method in typeof(RoomHub).GetMethods())
        {
            var attribute = method.GetCustomAttribute<HubMethodNameAttribute>();
            if (attribute is not null)
            {
                names.Should().Contain(attribute.Name);
            }
        }
    }

    [GeneratedRegex(@"(?<key>\w+): '(?<value>[^']+)'", RegexOptions.CultureInvariant)]
    private static partial Regex ContractPairs();
}
