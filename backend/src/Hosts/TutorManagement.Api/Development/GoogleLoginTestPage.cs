using System.Text.Encodings.Web;

namespace TutorManagement.Api.Development;

internal static class GoogleLoginTestPage
{
    internal static void MapGoogleLoginTestPage(this WebApplication app)
    {
        app.MapGet("/dev/google-login", (HttpContext context, IConfiguration configuration) =>
        {
            var clientId = configuration["GoogleAuth:ClientId"];
            if (string.IsNullOrWhiteSpace(clientId))
                return Results.Problem("Set GoogleAuth:ClientId in TutorManagement.Api User Secrets first.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);

            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            var encodedClientId = HtmlEncoder.Default.Encode(clientId);
            var html = $$"""
                <!doctype html>
                <html lang="vi">
                <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
                <title>Google login – local test</title>
                <style>body{font:16px system-ui;max-width:760px;margin:48px auto;padding:0 16px}
                textarea{width:100%;min-height:110px;margin:12px 0}pre{white-space:pre-wrap;overflow-wrap:anywhere}
                .note{color:#555}</style></head>
                <body><h1>Google login – chỉ Development</h1>
                <p class="note">Thêm origin của trang này vào Google Cloud Console. ID token và access token chỉ dùng để test;
                đừng chia sẻ hoặc lưu chúng. Refresh token được giữ trong HttpOnly cookie, không xuất hiện ở đây.</p>
                <div id="g_id_onload" data-client_id="{{encodedClientId}}"
                     data-callback="handleGoogleCredential" data-auto_prompt="false"></div>
                <div class="g_id_signin" data-type="standard"></div>
                <label for="idToken">ID token để thử trong Swagger:</label>
                <textarea id="idToken" readonly></textarea>
                <label for="result">Kết quả đăng nhập backend:</label><pre id="result"></pre>
                <script>
                async function handleGoogleCredential(response) {
                    document.getElementById('idToken').value = response.credential;
                    const output = document.getElementById('result');
                    try {
                        const api = await fetch('/api/v1/auth/google', {
                            method: 'POST', headers: {'Content-Type': 'application/json'},
                            credentials: 'same-origin', body: JSON.stringify({idToken: response.credential})
                        });
                        output.textContent = `${api.status} ${JSON.stringify(await api.json(), null, 2)}`;
                    } catch (error) { output.textContent = String(error); }
                }
                </script>
                <script src="https://accounts.google.com/gsi/client" async defer></script>
                </body></html>
                """;
            return Results.Content(html, "text/html; charset=utf-8");
        }).ExcludeFromDescription();
    }
}
