using System.Net.Http.Json;
using Fortuna.Server.Accounts;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Fortuna.Server.Tests.Support;

/// <summary>ログイン済みのテスト用ユーザー。HTTP は Cookie を保持し、Hub には Cookie ヘッダーを渡す。</summary>
public sealed class TestUser
{
    private TestUser(Guid id, string email, HttpClient http, string cookieHeader)
    {
        Id = id;
        Email = email;
        Http = http;
        CookieHeader = cookieHeader;
    }

    public Guid Id { get; }
    public string Email { get; }
    public HttpClient Http { get; }
    public string CookieHeader { get; }

    public static async Task<TestUser> RegisterAsync(FortunaFactory factory, string? prefix = null)
    {
        var email = $"{prefix ?? "user"}-{Guid.NewGuid():N}@example.test";

        using var anonymous = factory.CreateClient();
        var register = await anonymous.PostAsJsonAsync(
            "/api/auth/register", new { email, password = FortunaFactory.UserPassword });
        register.EnsureSuccessStatusCode();

        return await LoginAsync(factory, email, FortunaFactory.UserPassword);
    }

    public static async Task<TestUser> LoginAsync(FortunaFactory factory, string email, string password)
    {
        var http = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var login = await http.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email, password });
        login.EnsureSuccessStatusCode();

        var cookie = string.Join("; ", login.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));

        var me = await http.GetFromJsonAsync<AccountEndpoints.MeResponse>("/api/me");
        return new TestUser(me!.Id, email, http, cookie);
    }
}
