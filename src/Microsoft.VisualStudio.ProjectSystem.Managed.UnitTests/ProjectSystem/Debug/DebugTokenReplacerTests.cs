﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Build.Construction;
using Microsoft.VisualStudio.ProjectSystem.Utilities;
using Moq;
using Xunit;

namespace Microsoft.VisualStudio.ProjectSystem.Debug
{

    [ProjectSystemTrait]
    public class DebugTokenReplacerTests
    {
        Dictionary<string, string> _envVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "%env1%","envVariable1" },
            { "%env2%","envVariable2" },
            { "%env3%","$(msbuildProperty6)" }
        };
        
        Mock<IEnvironmentHelper> _envHelper = new Mock<IEnvironmentHelper>();
        public DebugTokenReplacerTests()
        {
            _envHelper.Setup(x => x.ExpandEnvironmentVariables(It.IsAny<string>())).Returns<string>((str) =>
            {
                foreach (var kv in _envVars)
                {
                    str = str.Replace(kv.Key, kv.Value);
                }
                return str;
            });

        }

        [Fact]
        public async Task DebugTokenReplacer_ReplaceTokensInProfileTests()
        {
            IUnconfiguredProjectCommonServices services = IUnconfiguredProjectCommonServicesFactory.Create();


            var replacer = new DebugTokenReplacerUnderTest(IUnconfiguredProjectCommonServicesFactory.Create(), _envHelper.Object);

            // Tests all the possible replacements. env3 tests that enviroment vars are resolved before msbuild tokens
            var launchProfile = new LaunchProfile()
            {
                Name = "$(msbuildProperty1)",
                CommandLineArgs = "%env1%",
                CommandName = "$(msbuildProperty2)",
                ExecutablePath = "$(test this string",  // Not a valid token
                WorkingDirectory = "c:\\test\\%env3%",
                LaunchBrowser = false,
                LaunchUrl = "http://localhost:8080/$(unknownproperty)",
                EnvironmentVariables = ImmutableDictionary<string, string>.Empty.Add("var1", "%env1%").Add("var2", "$(msbuildProperty3)"),
                OtherSettings = ImmutableDictionary<string, object>.Empty.Add("setting1", "%env1%").Add("setting2", true),
            };

            var resolvedProfile = await replacer.ReplaceTokensInProfileAsync(launchProfile);

            // Name and Command name should never be touched
            Assert.Equal("$(msbuildProperty1)", resolvedProfile.Name);
            Assert.Equal("$(msbuildProperty2)", resolvedProfile.CommandName);
            Assert.Equal("envVariable1", resolvedProfile.CommandLineArgs);
            Assert.Equal("$(test this string", resolvedProfile.ExecutablePath);
            Assert.Equal(false, resolvedProfile.LaunchBrowser);
            Assert.Equal("http://localhost:8080/", resolvedProfile.LaunchUrl);
            Assert.Equal("c:\\test\\Property6", resolvedProfile.WorkingDirectory);
            Assert.Equal("envVariable1", resolvedProfile.EnvironmentVariables["var1"]);
            Assert.Equal("Property3", resolvedProfile.EnvironmentVariables["var2"]);
            Assert.Equal("envVariable1", resolvedProfile.OtherSettings["setting1"]);
            Assert.Equal(true, resolvedProfile.OtherSettings["setting2"]);

        }
        
        [Fact]
        public async Task DebugTokenReplacer_ReplaceTokensInStringTests()
        {
            IUnconfiguredProjectCommonServices services = IUnconfiguredProjectCommonServicesFactory.Create();

            var replacer = new DebugTokenReplacerUnderTest(IUnconfiguredProjectCommonServicesFactory.Create(), _envHelper.Object);

            // Test msbuild vars
            string result = await replacer.ReplaceTokensInStringAsync("this is msbuild: $(msbuildProperty5) %env1%", true);
            Assert.Equal("this is msbuild: Property5 envVariable1", result);

            result = await replacer.ReplaceTokensInStringAsync("this is msbuild: $(msbuildProperty5) %env1%", false);
            Assert.Equal("this is msbuild: Property5 %env1%", result);

            result = await replacer.ReplaceTokensInStringAsync("this is msbuild: $(UnknownMsbuildProperty) %env1%", true);
            Assert.Equal("this is msbuild:  envVariable1", result);

            result = await replacer.ReplaceTokensInStringAsync("this is msbuild: $(UnknownMsbuildProperty) %Unknown%", true);
            Assert.Equal("this is msbuild:  %Unknown%", result);

            result = await replacer.ReplaceTokensInStringAsync("this is msbuild: %env3% $(msbuildProperty2) $(msbuildProperty3)", true);
            Assert.Equal("this is msbuild: Property6 Property2 Property3", result);

            result = await replacer.ReplaceTokensInStringAsync(null, true);
            Assert.True(result == null);
            result = await replacer.ReplaceTokensInStringAsync(" ", true);
            Assert.True(result == " ");
        }
    }

    internal class DebugTokenReplacerUnderTest : DebugTokenReplacer
    {
        public DebugTokenReplacerUnderTest(IUnconfiguredProjectCommonServices commonServices, IEnvironmentHelper envHelper)
            : base(commonServices, envHelper)
        {

        }

        protected override IProjectReadAccess AccessProject()
        {
            return new TestProjectReadAccessor();
        }

        class TestProjectReadAccessor : IProjectReadAccess
        {
            public TestProjectReadAccessor() { }
            public Task<Microsoft.Build.Evaluation.Project> GetProjectAsync() {return Task.FromResult(CreateMsBuildProject());}
            public void Dispose() { }

            private Microsoft.Build.Evaluation.Project CreateMsBuildProject()
            {
                // This is default. Can change it to match needs
                string projectFile = 
                @"<?xml version=""1.0"" encoding=""utf-16""?><Project xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
                <PropertyGroup>
                    <msbuildProperty1>Property1</msbuildProperty1>
                    <msbuildProperty2>Property2</msbuildProperty2>
                    <msbuildProperty3>Property3</msbuildProperty3>
                    <msbuildProperty4>Property4</msbuildProperty4>
                    <msbuildProperty5>Property5</msbuildProperty5>
                    <msbuildProperty6>Property6</msbuildProperty6>
                </PropertyGroup>
                </Project>";

                var settings = new XmlReaderSettings()
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    CloseInput = false,
                };
                using (var reader = XmlReader.Create(new System.IO.StringReader(projectFile), settings))
                {
                    ProjectRootElement importFile = ProjectRootElement.Create(reader); 
                    return new Microsoft.Build.Evaluation.Project(importFile);
                }
            }
        }

    }
}
