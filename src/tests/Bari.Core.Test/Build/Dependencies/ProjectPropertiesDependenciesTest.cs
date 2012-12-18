﻿using System.IO;
using Bari.Core.Build.Dependencies;
using Bari.Core.Build.Dependencies.Protocol;
using Bari.Core.Model;
using Bari.Core.Test.Helper;
using FluentAssertions;
using NUnit.Framework;

namespace Bari.Core.Test.Build.Dependencies
{
    [TestFixture]
    public class ProjectPropertiesDependenciesTest
    {
        private Project project;

        [SetUp]
        public void SetUp()
        {
            project = new Project("test", new Module("testmod", new Suite(new TestFileSystemDirectory("modroot"))))
                {
                    Type = ProjectType.Library
                };
        }

        [Test]
        public void CreatesSameFingerprintForSameState()
        {
            var dep = new ProjectPropertiesDependencies(project, "Type");
            var fp1 = dep.CreateFingerprint();
            var fp2 = dep.CreateFingerprint();

            fp1.Should().Be(fp2);
            fp2.Should().Be(fp1);
        }

        [Test]
        public void ChangingThePropertyChangesTheFingerprint()
        {
            var dep = new ProjectPropertiesDependencies(project, "Type");
            var fp1 = dep.CreateFingerprint();

            project.Type = ProjectType.Executable;

            var fp2 = dep.CreateFingerprint();

            fp1.Should().NotBe(fp2);
        }

        [Test]
        public void ConvertToProtocolAndBack()
        {
            var dep = new ProjectPropertiesDependencies(project, "Type");
            var fp1 = dep.CreateFingerprint();

            var proto = fp1.Protocol;
            var fp2 = proto.CreateFingerprint();

            fp1.Should().Be(fp2);
        }

        [Test]
        public void SerializeAndReadBack()
        {
            var ser = new BinarySerializer();
            var dep = new ProjectPropertiesDependencies(project, "Type");
            var fp1 = dep.CreateFingerprint();

            byte[] data;
            using (var ms = new MemoryStream())
            {
                fp1.Save(ser, ms);
                data = ms.ToArray();
            }

            ObjectPropertiesFingerprint fp2;
            using (var ms = new MemoryStream(data))
            {
                fp2 = new ObjectPropertiesFingerprint(ser, ms);
            }

            fp1.Should().Be(fp2);
        }
    }
}