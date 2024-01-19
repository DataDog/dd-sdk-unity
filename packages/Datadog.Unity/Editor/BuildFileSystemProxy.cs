// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System.IO;

namespace Datadog.Unity.Editor
{
    // Used to access the file system during the build process. Created
    // mostly to allow for mocking during unit tests.
    public interface IBuildFileSystemProxy
    {
        bool FileExists(string path);
        void DeleteFile(string path);
        void CopyFile(string sourcePath, string destinationPath);
        void CreateDirectory(string path);
        bool DirectoryExists(string path);
        void WriteAllText(string path, string contents);
    }

    public class DefaultBuildFileSystemProxy : IBuildFileSystemProxy
    {
        public bool FileExists(string path)
        {
            return File.Exists(path);
        }

        public void DeleteFile(string path)
        {
            File.Delete(path);
        }

        public void CopyFile(string sourcePath, string destinationPath)
        {
            File.Copy(sourcePath, destinationPath);
        }

        public void CreateDirectory(string path)
        {
            Directory.CreateDirectory(path);
        }

        public bool DirectoryExists(string path)
        {
            return Directory.Exists(path);
        }

        public void WriteAllText(string path, string contents)
        {
            File.WriteAllText(path, contents);
        }
    }
}
