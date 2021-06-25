using Orangebeard.Shared.Configuration;
using System.Collections.Generic;

namespace Orangebeard.VSTest.TestLogger.Configuration
{
    class ConfigProvider : IConfigurationProvider
    {
        private Dictionary<string, string> _parameters;

        public ConfigProvider(Dictionary<string, string> parameters)
        {
            _parameters = parameters;
        }

        public IDictionary<string, string> Properties { get; } = new Dictionary<string, string>();

        public IDictionary<string, string> Load()
        {
            foreach (var parameter in _parameters)
            {
                var key = parameter.Key.ToLowerInvariant().Replace(".", ConfigurationPath.KeyDelimeter);
                var value = parameter.Value;

                if (key == $"launch{ConfigurationPath.KeyDelimeter}attributes")
                {
                    value = parameter.Value.Replace(",", ";");
                }

                Properties[key] = value;
            }

            return Properties;
        }
    }
}
