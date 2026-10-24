using System.Net;
using System.Net.Http.Json;
using static RekruTime.Tests.TestApp;

namespace RekruTime.Tests;

// Club:PublicUrl with a path (https://kolo.pl/rekrutacja) → the app answers under that prefix and its login cookie works there.
public class SubPathTests() : IntegrationTest(new Dictionary<string, string?> { ["Club:PublicUrl"] = "https://kn.test/rekrutacja" })
{
    [Fact]
    public async Task Api_and_login_work_under_the_public_url_path()
    {
        var client = App.CreateClient();
        Assert.Equal(HttpStatusCode.OK, await Status(client.GetAsync("/rekrutacja/api/health")));

        await Json(client.PostAsJsonAsync("/rekrutacja/api/auth/login", new { email = AdminEmail, password = AdminPassword }));
        var me = await Json(client.GetAsync("/rekrutacja/api/auth/me"));
        Assert.Equal(AdminEmail, me.GetProperty("email").GetString());

        Assert.Equal(HttpStatusCode.NotFound, await Status(client.GetAsync("/rekrutacja/api/nope")));
    }
}
