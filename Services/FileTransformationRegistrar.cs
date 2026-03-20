using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
                var ftAssembly = AssemblyLoadContext.All
                    .SelectMany(x => x.Assemblies)
                    .FirstOrDefault(x => x.FullName?.Contains(".FileTransformation") ?? false);

                if (ftAssembly == null)
                {
                    _logger.LogWarning("[SkinManager] File Transformation plugin not found.");
                    return;
                }

                var pluginInterfaceType = ftAssembly.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface");
                if (pluginInterfaceType == null)
                {
                    _logger.LogWarning("[SkinManager] Could not find PluginInterface in File Transformation assembly.");
                    return;
                }

                var newtonsoftAssembly = AssemblyLoadContext.All
                    .SelectMany(x => x.Assemblies)
                    .FirstOrDefault(x => x.GetName().Name == "Newtonsoft.Json"
                                      && x != typeof(FileTransformationRegistrar).Assembly);

                if (newtonsoftAssembly == null)
                {
                    newtonsoftAssembly = AssemblyLoadContext.All
                        .SelectMany(x => x.Assemblies)
                        .FirstOrDefault(x => x.GetName().Name == "Newtonsoft.Json");
                }

                if (newtonsoftAssembly == null)
                {
                    _logger.LogWarning("[SkinManager] Could not find Newtonsoft.Json assembly.");
                    return;
                }

                var jobjectType = newtonsoftAssembly.GetType("Newtonsoft.Json.Linq.JObject");
                var payload = Activator.CreateInstance(jobjectType);

                var jtokenType = newtonsoftAssembly.GetType("Newtonsoft.Json.Linq.JToken");
                var fromObject = jtokenType.GetMethod("FromObject", new[] { typeof(object) });

                var indexerSetter = jobjectType.GetProperty("Item", new[] { typeof(string) })
                                               ?.GetSetMethod();

                void Set(string key, object value)
                {
                    var token = fromObject.Invoke(null, new[] { value });
                    indexerSetter.Invoke(payload, new[] { key, token });
                }

                Set("id", TransformationId.ToString());
                Set("fileNamePattern", "index.html");
                Set("callbackAssembly", typeof(SkinInjector).Assembly.FullName);
                Set("callbackClass", typeof(SkinInjector).FullName);
                Set("callbackMethod", nameof(SkinInjector.InjectTheme));

                pluginInterfaceType.GetMethod("RegisterTransformation")
                    ?.Invoke(null, new[] { payload });

                _logger.LogInformation("[SkinManager] Successfully registered CSS injection with File Transformation.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SkinManager] Failed to register with File Transformation.");
            }
        }
    }
}