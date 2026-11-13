using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using UnityNuGet.Npm;

namespace UnityNuGet.Server.Tests
{
    public class ApiControllerTests
    {
        private readonly UnityNuGetWebApplicationFactory _webApplicationFactory;

        public ApiControllerTests()
        {
            _webApplicationFactory = new UnityNuGetWebApplicationFactory();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _webApplicationFactory.Dispose();
        }

        [Test]
        public async Task Home_Success()
        {
            using HttpClient client = _webApplicationFactory.CreateDefaultClient();

            HttpResponseMessage response = await client.GetAsync("/");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Found));
                Assert.That(response.Headers.Location, Is.EqualTo(new Uri("/-/all", UriKind.Relative)));
            }

            string responseContent = await response.Content.ReadAsStringAsync();

            Assert.That(responseContent, Is.Empty);
        }

        [Test]
        public async Task GetAll_Success()
        {
            using HttpClient httpClient = _webApplicationFactory.CreateDefaultClient();

            await WaitForInitialization(_webApplicationFactory.Services);

            HttpResponseMessage response = await httpClient.GetAsync("/-/all");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            string responseContent = await response.Content.ReadAsStringAsync();

            NpmPackageListAllResponse npmPackageListAllResponse = JsonSerializer.Deserialize(responseContent, UnityNuGetJsonSerializerContext.Default.NpmPackageListAllResponse)!;

            Assert.That(npmPackageListAllResponse.Packages, Has.Count.EqualTo(1));

            using (Assert.EnterMultipleScope())
            {
                string packageName = $"org.nuget.{UnityNuGetWebApplicationFactory.PackageName.ToLowerInvariant()}";

                Assert.That(npmPackageListAllResponse.Packages.ContainsKey(packageName), Is.True);
                Assert.That(npmPackageListAllResponse.Packages[packageName].Name, Is.EqualTo(packageName));
                Assert.That(npmPackageListAllResponse.Packages[packageName].Description, Is.Not.Null);
                Assert.That(npmPackageListAllResponse.Packages[packageName].Author, Is.Not.Null);
            }
        }

        [Test]
        public async Task GetPackage_NotFound()
        {
            using HttpClient httpClient = _webApplicationFactory.CreateDefaultClient();

            await WaitForInitialization(_webApplicationFactory.Services);

            HttpResponseMessage response = await httpClient.GetAsync($"/InvalidPackageName");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            string responseContent = await response.Content.ReadAsStringAsync();

            NpmError npmError = JsonSerializer.Deserialize(responseContent, UnityNuGetJsonSerializerContext.Default.NpmError)!;

            Assert.That(npmError.Error, Is.EqualTo("not_found"));
        }

        [Test]
        public async Task GetPackage_Success()
        {
            using HttpClient httpClient = _webApplicationFactory.CreateDefaultClient();

            await WaitForInitialization(_webApplicationFactory.Services);

            string packageName = $"org.nuget.{UnityNuGetWebApplicationFactory.PackageName.ToLowerInvariant()}";

            HttpResponseMessage response = await httpClient.GetAsync($"/{packageName}");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            string responseContent = await response.Content.ReadAsStringAsync();

            NpmPackage npmPackage = JsonSerializer.Deserialize(responseContent, UnityNuGetJsonSerializerContext.Default.NpmPackage)!;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(npmPackage.Id, Is.EqualTo(packageName));
                Assert.That(npmPackage.Revision, Is.Not.Null);
                Assert.That(npmPackage.Name, Is.EqualTo(packageName));
                Assert.That(npmPackage.License, Is.Not.Null);
                Assert.That(npmPackage.Description, Is.Not.Null);
            }
        }

        [Test]
        [TestCase("org.nuget.newtonsoft.json", "InvalidFile")]
        [TestCase("InvalidId", "org.nuget.newtonsoft.json-11.0.1.tgz")]
        [TestCase("org.nuget.newtonsoft.json", "org.nuget.newtonsoft.json_11.0.1.tgz")]
        [TestCase("org.nuget.newtonsoft.json", "org.nuget.newtonsoft.json-11.0.1.InvalidExtension")]
        public async Task DownloadPackage_NotFound(string id, string file)
        {
            using HttpClient httpClient = _webApplicationFactory.CreateDefaultClient();

            await WaitForInitialization(_webApplicationFactory.Services);

            HttpResponseMessage response = await httpClient.GetAsync($"/{id}/-/{file}");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            string responseContent = await response.Content.ReadAsStringAsync();

            NpmError npmError = JsonSerializer.Deserialize(responseContent, UnityNuGetJsonSerializerContext.Default.NpmError)!;

            Assert.That(npmError.Error, Is.EqualTo("not_found"));
        }

        [Test]
        public async Task DownloadPackage_Head_Success()
        {
            using HttpClient httpClient = _webApplicationFactory.CreateDefaultClient();

            await WaitForInitialization(_webApplicationFactory.Services);

            string packageName = $"org.nuget.{UnityNuGetWebApplicationFactory.PackageName.ToLowerInvariant()}";

            HttpRequestMessage httpRequestMessage = new()
            {
                RequestUri = new Uri($"/{packageName}/-/{packageName}-11.0.1.tgz", UriKind.Relative),
                Method = HttpMethod.Head
            };

            HttpResponseMessage response = await httpClient.SendAsync(httpRequestMessage);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            byte[] responseContent = await response.Content.ReadAsByteArrayAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(responseContent, Is.Empty);

                Assert.That(response.Content.Headers.ContentType!.MediaType, Is.EqualTo("application/octet-stream"));
                Assert.That(response.Content.Headers.ContentLength, Is.GreaterThan(0));
            }
        }

        [Test]
        public async Task DownloadPackage_Get_Success()
        {
            using HttpClient httpClient = _webApplicationFactory.CreateDefaultClient();

            await WaitForInitialization(_webApplicationFactory.Services);

            string packageName = $"org.nuget.{UnityNuGetWebApplicationFactory.PackageName.ToLowerInvariant()}";

            HttpResponseMessage response = await httpClient.GetAsync($"/{packageName}/-/{packageName}-11.0.1.tgz");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            byte[] responseContent = await response.Content.ReadAsByteArrayAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(responseContent, Is.Not.Empty);

                Assert.That(response.Content.Headers.ContentType!.MediaType, Is.EqualTo("application/octet-stream"));
                Assert.That(response.Content.Headers.ContentLength, Is.GreaterThan(0));
            }
        }

        [Test]
        public async Task Status_ShowsProgressAndReportMessages()
        {
            var cache = new RegistryCacheSingleton { ProgressPackageIndex = 1, ProgressTotalPackageCount = 4 };
            var report = new RegistryCacheReport(cache, Options.Create(new RegistryOptions()));

            using WebApplicationFactory<Program> factory = _webApplicationFactory
                .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IHostedService>();
                    services.AddSingleton(report);
                }));

            using HttpClient client = factory.CreateDefaultClient();

            using HttpResponseMessage emptyResponse = await client.GetAsync("/status");

            string emptyContent = await emptyResponse.Content.ReadAsStringAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(emptyResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(emptyContent, Does.Contain("<h1>Time remaining for the next update: </h1>"));
                Assert.That(emptyContent, Does.Not.Contain("<h1>Error messages</h1>"));
                Assert.That(emptyContent, Does.Not.Contain("<h1>Warning messages</h1>"));
                Assert.That(emptyContent, Does.Not.Contain("<h1>Information messages</h1>"));
            }

            report.Start();
            report.AddError("Error one");
            report.AddError("Error two");
            report.AddWarning("Warning one");
            report.AddInformation("Information one");

            using HttpResponseMessage runningResponse = await client.GetAsync("/status");

            string runningContent = await runningResponse.Content.ReadAsStringAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(runningResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(runningContent, Does.Contain($"<h1>The server is currently updating ({25.0:F1}% completed)...</h1>"));
                Assert.That(runningContent, Does.Contain("<h1>Error messages</h1>"));
                Assert.That(runningContent, Does.Contain("<p>Error one</p>"));
                Assert.That(runningContent, Does.Contain("<p>Error two</p>"));
                Assert.That(runningContent, Does.Contain("<h1>Warning messages</h1>"));
                Assert.That(runningContent, Does.Contain("<p>Warning one</p>"));
                Assert.That(runningContent, Does.Contain("<h1>Information messages</h1>"));
                Assert.That(runningContent, Does.Contain("<p>Information one</p>"));
            }

            report.Complete();

            using HttpResponseMessage completedResponse = await client.GetAsync("/status");

            string completedContent = await completedResponse.Content.ReadAsStringAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(completedResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(completedContent, Does.Contain("<h1>Time remaining for the next update: 00:00:00</h1>"));
                Assert.That(completedContent, Does.Contain("<p>Error one</p>"));
            }
        }

        private static async Task WaitForInitialization(IServiceProvider serviceProvider)
        {
            RegistryCacheSingleton registryCacheSingleton = serviceProvider.GetRequiredService<RegistryCacheSingleton>();

            while (registryCacheSingleton.Instance == null)
            {
                await Task.Delay(25);
            }
        }
    }
}
