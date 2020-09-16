﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Mobile.BuildTools.Build;
using Mobile.BuildTools.Generators.Images;
using Mobile.BuildTools.Utils;

namespace Mobile.BuildTools.Tasks
{
    public class CollectImageAssetsTask : BuildToolsTaskBase
    {
        public string AdditionalSearchPaths { get; set; }

        public bool? IgnoreDefaultSearchPaths { get; set; }

        [Output]
        public ITaskItem[] GeneratedImages { get; private set; }

        [Output]
        public ITaskItem[] SourceImages { get; private set; }

        [Output]
        public bool HasImages => GeneratedImages.Length > 0;

        internal override void ExecuteInternal(IBuildConfiguration config)
        {
// #if DEBUG
//             if (!System.Diagnostics.Debugger.IsAttached)
//                 System.Diagnostics.Debugger.Launch();
//             else
//                 System.Diagnostics.Debugger.Break();
// #endif

            GeneratedImages = Array.Empty<ITaskItem>();
            var generator = CreateGenerator(config.Platform, config);
            generator.SearchFolders = GetSearchPaths(config);
            if (generator is null)
            {
                Log.LogWarning($"Cannot collect image assets for {TargetFrameworkIdentifier}, target framework is not supported.");
                return;
            }

            generator.Execute();
            SourceImages = generator.ImageInputFiles.Select(x => new TaskItem(x)).ToArray();
            GeneratedImages = generator.Outputs.Select(x => x.ToTaskItem()).ToArray();
        }

        private ImageCollectionGeneratorBase CreateGenerator(Platform platform, IBuildConfiguration config)
        {
            switch(platform)
            {
                case Platform.Android:
                    return new AndroidImageCollectionGenerator(this);
                case Platform.iOS:
                case Platform.macOS:
                case Platform.TVOS:
                    return new AppleImageCollectionGenerator(this);
                case Platform.UWP:
                    return new UwpImageCollectionGenerator(this);
                default: return null;
            }
        }

        internal IEnumerable<string> GetSearchPaths(IBuildConfiguration config)
        {
            return ImageSearchUtil.GetSearchPaths(config.Configuration, config.Platform, config.BuildConfiguration, ConfigurationPath, AdditionalSearchPaths, IgnoreDefaultSearchPaths);
        }
    }
}
