FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
RUN dotnet restore BudgetAdvisor.slnx
RUN dotnet publish src/BudgetAdvisor.App/BudgetAdvisor.App.csproj -c Release -o /app/client-publish

RUN mkdir -p /host && dotnet new web -n StaticHost -o /host --framework net10.0
RUN printf '%s\n' \
    'using Microsoft.AspNetCore.StaticFiles;' \
    'using Microsoft.Extensions.FileProviders;' \
    'var builder = WebApplication.CreateBuilder(args);' \
    'var app = builder.Build();' \
    'app.Use(async (context, next) =>' \
    '{' \
    '    context.Response.Headers["X-Budget-Advisor-Container"] = "budget-advisor-local";' \
    '    await next();' \
    '    Console.WriteLine($"{DateTime.UtcNow:O} {context.Request.Method} {context.Request.Path} -> {context.Response.StatusCode}");' \
    '});' \
    'var contentTypeProvider = new FileExtensionContentTypeProvider();' \
    'contentTypeProvider.Mappings[".dat"] = "application/octet-stream";' \
    'app.UseStaticFiles(new StaticFileOptions' \
    '{' \
    '    ContentTypeProvider = contentTypeProvider,' \
    '    OnPrepareResponse = context =>' \
    '    {' \
    '        context.Context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";' \
    '        context.Context.Response.Headers.Pragma = "no-cache";' \
    '        context.Context.Response.Headers.Expires = "0";' \
    '    }' \
    '});' \
    'app.MapGet("/auth/dropbox/callback", async context =>' \
    '{' \
    '    var file = Path.Combine(app.Environment.WebRootPath, "auth", "dropbox", "callback", "index.html");' \
    '    context.Response.ContentType = "text/html; charset=utf-8";' \
    '    await context.Response.SendFileAsync(file);' \
    '});' \
    'app.MapFallbackToFile("index.html");' \
    'app.Run();' > /host/Program.cs
RUN cp -r /app/client-publish/wwwroot/. /host/wwwroot/
RUN dotnet publish /host/StaticHost.csproj -c Release -o /app/host-publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

COPY --from=build /app/host-publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "StaticHost.dll"]
