using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Mobile.BuildTools.Build;
using Mobile.BuildTools.Models.Secrets;
using Mobile.BuildTools.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Mobile.BuildTools.Generators.Secrets
{
    internal class SecretsClassGenerator : GeneratorBase<ITaskItem>
    {
#pragma warning disable IDE1006, IDE0040
        private const string AutoGeneratedMessage =
@"// ------------------------------------------------------------------------------
//  <autogenerated>
//      This code was generated by Mobile.BuildTools. For more information or to
//      file an issue please see https://github.com/dansiegel/Mobile.BuildTools
//
//      Changes to this file may cause incorrect behavior and will be lost when 
//      the code is regenerated.
//
//      When I wrote this, only God and I understood what I was doing
//      Now, God only knows.
//
//      NOTE: This file should be excluded from source control.
//  </autogenerated>
// ------------------------------------------------------------------------------

";

        private const string SafePlaceholder = "*****";

        private const string TabSpace = "    ";

        private static readonly IReadOnlyDictionary<PropertyType, string> PropertyTypeMappings = new Dictionary<PropertyType, string>
        {
            { PropertyType.Bool, "{0}" },
            { PropertyType.DateTime, "DateTime.Parse(\"{0}\")" }
        };

        public SecretsClassGenerator(IBuildConfiguration buildConfiguration)
            : base(buildConfiguration)
        {
        }
#pragma warning restore IDE1006, IDE0040

        public string ConfigurationSecretsJsonFilePath { get; set; }

        public string SecretsJsonFilePath { get; set; }

        public string BaseNamespace { get; set; }

        protected override void ExecuteInternal()
        {
            var secrets = GetMergedSecrets();

            var replacement = string.Empty;
            var safeReplacement = string.Empty;
            var secretsConfig = Build.GetSecretsConfig();
            bool saveConfig = secretsConfig is null;
            if(saveConfig)
            {
                secretsConfig = new SecretsConfig()
                {
                    ClassName = "Secrets",
                    Namespace = "Helpers",
                    Delimiter = ";",
                    Prefix = "BuildTools_"
                };
            }

            foreach (var secret in secrets)
            {
                replacement += ProcessSecret(secret, secretsConfig, saveConfig);
                safeReplacement += ProcessSecret(secret, secretsConfig, false, true);
            }

            if(saveConfig)
            {
                if (Build.Configuration.ProjectSecrets is null)
                {
                    Build.Configuration.ProjectSecrets = new Dictionary<string, SecretsConfig>();
                }
                Build.Configuration.ProjectSecrets.Add(Build.ProjectName, secretsConfig);
                Build.SaveConfiguration();
            }

            if(string.IsNullOrEmpty(secretsConfig.Namespace))
            {
                secretsConfig.Namespace = "Helpers";
            }

            if(string.IsNullOrEmpty(secretsConfig.ClassName))
            {
                secretsConfig.ClassName = "Secrets";
            }

            replacement = Regex.Replace(replacement, "\n\n$", "");

            var secretsClass = GenerateClass(replacement, secretsConfig);
            Log.LogMessage(Build.Configuration.Debug ? secretsClass : GenerateClass(Regex.Replace(safeReplacement, "\n\n$", ""), secretsConfig));

            var namespacePath = string.Join($"{Path.PathSeparator}", secretsConfig.Namespace.Split('.').Where(x => !string.IsNullOrEmpty(x)));
            var projectFile = Path.Combine(Build.ProjectDirectory, namespacePath, $"{secretsConfig.ClassName}.cs");
            var intermediateFile = Path.Combine(Build.IntermediateOutputPath, $"{secretsConfig.ClassName}.cs");
            var outputFile = File.Exists(projectFile) ? projectFile : intermediateFile;
            Log.LogMessage($"Writing Secrets Class to: '{outputFile}'");
            var generatedFile = new TaskItem(outputFile);
            generatedFile.SetMetadata("Visible", bool.TrueString);
            generatedFile.SetMetadata("Link", projectFile);
            Outputs = generatedFile;

            File.WriteAllText(outputFile, secretsClass);
        }

        internal JObject GetMergedSecrets()
        {
            JObject secrets = null;
            CreateOrMerge(SecretsJsonFilePath, ref secrets);
            CreateOrMerge(ConfigurationSecretsJsonFilePath, ref secrets);

            if(secrets is null)
            {
                throw new Exception("An unexpected error occurred. Could not locate any secrets.");
            }

            return secrets;
        }

        internal void CreateOrMerge(string jsonFilePath, ref JObject secrets)
        {
            if(File.Exists(jsonFilePath))
            {
                var json = File.ReadAllText(jsonFilePath);
                if(secrets is null)
                {
                    secrets = JObject.Parse(json);
                }
                else
                {
                    foreach(var pair in secrets)
                    {
                        secrets[pair.Key] = pair.Value;
                    }
                }
            }
        }

        internal string GenerateClass(string replacement, SecretsConfig secretsConfig) =>
            $"{AutoGeneratedMessage}\n\nusing System;\n\nnamespace {GetNamespace(secretsConfig.Namespace)}\n{{\n{TabSpace}internal static class {secretsConfig.ClassName}\n{TabSpace}{{\n{replacement}\n{TabSpace}}}\n}}\n";

        internal string ProcessSecret(KeyValuePair<string, JToken> secret, SecretsConfig secretsConfig, bool saveOutput, bool safeOutput = false)
        {
            //var valueConfig = secretsConfig.ContainsKey(secret.Key) ? secretsConfig[secret.Key] : null;
            if (!secretsConfig.HasKey(secret.Key, out var valueConfig) && !saveOutput)
            {
                return null;
            }

            if(valueConfig is null)
            {
                valueConfig = GenerateValueConfig(secret, secretsConfig);
                secretsConfig.Properties.Add(valueConfig);
            }

            var mapping = valueConfig.PropertyType.GetPropertyTypeMapping();
            return PropertyBuilder(secret, mapping.Type, mapping.Format, valueConfig.IsArray, safeOutput);
        }

        internal ValueConfig GenerateValueConfig(KeyValuePair<string, JToken> secret, SecretsConfig config)
        {
            var value = secret.Value.ToString();
            var valueArray = Regex.Split(value, $"(?<!\\\\){config.Delimiter}").Select(x => x.Replace($"\\{config.Delimiter}", config.Delimiter));
            bool isArray = false;
            if (valueArray.Count() > 1)
            {
                value = valueArray.FirstOrDefault();
            }
            var type = PropertyType.String;

            if(bool.TryParse(value, out _))
            {
                type = PropertyType.Bool;
            }
            else if (Regex.IsMatch(value, @"\d+\.\d+") && double.TryParse(value, out _))
            {
                type = PropertyType.Double;
            }
            else if (int.TryParse(value, out _))
            {
                type = PropertyType.Int;
            }

            return new ValueConfig
            {
                Name = secret.Key,
                IsArray = isArray,
                PropertyType = type
            };
        }

        internal string GetNamespace(string relativeNamespace)
        {
            var parts = relativeNamespace.Split('.', '/', '\\').Where(x => !string.IsNullOrEmpty(x));
            var count = parts.Count();
            if (count == 0)
            {
                return BaseNamespace;
            }
            else if(count == 1)
            {
                relativeNamespace = parts.First();
            }
            else if (count > 1)
            {
                relativeNamespace = string.Join(".", parts);
            }

            return $"{BaseNamespace}.{relativeNamespace}";
        }

        internal string PropertyBuilder(KeyValuePair<string, JToken> secret, Type type, string propertyFormat, bool isArray, bool safeOutput)
        {
            var output = string.Empty;
            var typeDeclaration = type.GetStandardTypeName();
            var accessModifier = type.FullName == typeDeclaration ? "static readonly" : "const";
            if (isArray)
            {
                typeDeclaration += "[]";
                var valueArray = GetValueArray(secret.Value).Select(x => string.Format(propertyFormat, GetOutputValue(x, safeOutput)));
                output = "new[] { " + string.Join(", ", valueArray) + " }";
            }
            else
            {
                output = string.Format(propertyFormat, GetOutputValue(secret.Value, safeOutput));
            }

            if(type == typeof(bool))
            {
                output = output.ToLower();
            }

            return $"{TabSpace}{TabSpace}internal {accessModifier} {typeDeclaration} {secret.Key} = {output};\n\n";
        }

        internal static IEnumerable<string> GetValueArray(JToken token) =>
            Regex.Split(token.ToString(), "(?<!\\\\);").Select(x => x.Replace("\\;", ";"));

        private string GetOutputValue(JToken value, bool safeOutput) =>
            GetOutputValue(value.ToString(), safeOutput);

        private string GetOutputValue(string value, bool safeOutput) =>
            safeOutput ? SafePlaceholder : value.ToString();

        
    }
}