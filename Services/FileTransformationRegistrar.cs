using System;
using System.Linq;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.SkinManager.Services
{
    public class FileTransformationRegistrar : IHostedService
    {
        private static readonly Guid TransformationId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");

        private readonly ILogger<FileTransformationRegistrar> _logger;

        public FileTransformationRegistrar(ILogger<FileTransformationRegistrar> logger)
        {
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            RegisterWithFileTransformation();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private void RegisterWithFileTransformation()
        {
            try
            {
                var fileTransformationAssembly = AssemblyLoadContext.All
                    .SelectMany(x => x.Assemblies)
                    .FirstOrDefault(x => x.FullName?.Contains(".FileTransformation") ?? false);

                if (fileTransformationAssembly == null)
                {
                    _logger.LogWarning("[SkinManager] File Transformation plugin not found. Install it and restart Jellyfin.");
                    return;
                }

                var pluginInterfaceType = fileTransformationAssembly.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface");
                if (pluginInterfaceType == null)
                {
                    _logger.LogWarning("[SkinManager] Could not find PluginInterface in File Transformation assembly.");
                    return;
                }

                var payload = new JObject
                {
                    { "id",               TransformationId.ToString() },
                    { "fileNamePattern",  "index.html" },
                    { "callbackAssembly", typeof(SkinInjector).Assembly.FullName },
                    { "callbackClass",    typeof(SkinInjector).FullName },
                    { "callbackMethod",   nameof(SkinInjector.InjectTheme) }
                };

                pluginInterfaceType.GetMethod("RegisterTransformation")
                    ?.Invoke(null, new object[] { payload });

                _logger.LogInformation("[SkinManager] Successfully registered CSS injection with File Transformation.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SkinManager] Failed to register with File Transformation.");
            }
        }
    }
}