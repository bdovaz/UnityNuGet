using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using UnityNuGet;
using UnityNuGet.Npm;

namespace UnityNuGet.Server.Tests
{
    public class AllowServingWithMissingDependenciesTests
    {
        private readonly AllowServingWithMissingDependenciesWebApplicationFactory _webApplicationFactory;

        public AllowServingWithMissingDependenciesTests()
        {
            _webApplicationFactory = new AllowServingWithMissingDependenciesWebApplicationFactory();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _webApplicationFactory.Dispose();
        }

        [Test]
        public async Task GetAll_Succeeds_WhenBuildHasErrors()
        {
            using HttpClient httpClient = _webApplicationFactory.CreateDefaultClient();

            await WaitForInitialization(_webApplicationFactory.Services, TimeSpan.FromMinutes(2));

            RegistryCacheReport registryCacheReport = _webApplicationFactory.Services.GetRequiredService<RegistryCacheReport>();
            Assert.That(registryCacheReport.ErrorMessages.Any(), Is.True, "Expected build errors but none were reported.");

            HttpResponseMessage response = await httpClient.GetAsync("/-/all");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            string responseContent = await response.Content.ReadAsStringAsync();
            NpmPackageListAllResponse npmPackageListAllResponse = JsonSerializer.Deserialize(
                responseContent,
                UnityNuGetJsonSerializerContext.Default.NpmPackageListAllResponse)!;

            Assert.That(npmPackageListAllResponse, Is.Not.Null);
        }

        private static async Task WaitForInitialization(IServiceProvider serviceProvider, TimeSpan timeout)
        {
            RegistryCacheSingleton registryCacheSingleton = serviceProvider.GetRequiredService<RegistryCacheSingleton>();

            DateTime deadline = DateTime.UtcNow.Add(timeout);

            while (registryCacheSingleton.Instance == null)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    Assert.Fail("Timed out waiting for RegistryCache initialization.");
                }

                await Task.Delay(50);
            }
        }
    }

    internal class AllowServingWithMissingDependenciesWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _rootPersistentFolder;
        private readonly string _registryFilePath;

        public AllowServingWithMissingDependenciesWebApplicationFactory()
        {
            _rootPersistentFolder = Path.Combine(Path.GetTempPath(), "UnityNuGet.Server.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootPersistentFolder);

            _registryFilePath = Path.Combine(AppContext.BaseDirectory, "registry.test.missing-dep.json");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureAppConfiguration(configBuilder =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { WebHostDefaults.ServerUrlsKey, "http://localhost" },
                    { "Registry:RegistryFilePath", _registryFilePath },
                    { "Registry:RootPersistentFolder", _rootPersistentFolder },
                    { "Registry:AllowServingWithMissingDependencies", "true" },
                    { "Registry:Filter", "Serilog.Sinks.File" }
                });
            });
        }
    }
}
