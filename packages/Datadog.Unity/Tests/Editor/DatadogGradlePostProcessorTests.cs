// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using NUnit.Framework;

namespace Datadog.Unity.Editor.Tests
{
    public class DatadogGradlePostProcessorTests
    {
        [Test]
        public void ModifiesAndroidxMetricsDependencyIfRumDependencyIsDeclared()
        {
            string[] lines = GradleFileWithDatadogDependencies.Split("\n");
            string[] gotLines = DatadogGradlePostProcessor.ApplyAndroidxMetricsCompatibilityFix(lines);
            string got = string.Join("\n", gotLines);
            Assert.AreEqual(GradleFileAsModifiedByDatadogGradlePostProcessor, got);
        }

        [Test]
        public void HasNoEffectWhenRunAgain()
        {
            string[] lines = GradleFileAsModifiedByDatadogGradlePostProcessor.Split("\n");
            string[] gotLines = DatadogGradlePostProcessor.ApplyAndroidxMetricsCompatibilityFix(lines);
            Assert.AreEqual(lines, gotLines);
        }

        [Test]
        public void HasNoEffectIfDatadogSectionDoesNotExist()
        {
            string[] lines = GradleFileWithoutDatadogDependencies.Split("\n");
            string[] gotLines = DatadogGradlePostProcessor.ApplyAndroidxMetricsCompatibilityFix(lines);
            Assert.AreEqual(lines, gotLines);
        }

        [Test]
        public void WritesDatadogDependencyDeclarationsAfterFileTreeAnchor()
        {
            string[] lines = GeneratedGradleFileBeforeDatadogDependencies.Split("\n");
            string[] gotLines = DatadogGradlePostProcessor.ApplyDatadogDependencies(
                lines, "3.10.0", new[] { "dd-sdk-android-rum", "dd-sdk-android-logs", "dd-sdk-android-ndk" });
            string got = string.Join("\n", gotLines);
            Assert.AreEqual(GradleFileWithDatadogDependencies, got);
        }

        [Test]
        public void DependencyWriteHasNoEffectWhenRunAgain()
        {
            string[] lines = GradleFileWithDatadogDependencies.Split("\n");
            string[] gotLines = DatadogGradlePostProcessor.ApplyDatadogDependencies(
                lines, "3.10.0", new[] { "dd-sdk-android-rum", "dd-sdk-android-logs", "dd-sdk-android-ndk" });
            Assert.AreEqual(lines, gotLines);
        }

        [Test]
        public void DependencyWriteHasNoEffectWithoutFileTreeAnchor()
        {
            string[] lines =
            {
                "dependencies {",
                "    implementation 'com.example:some-other-dependency:4.13.0'",
                "}",
            };
            string[] gotLines = DatadogGradlePostProcessor.ApplyDatadogDependencies(
                lines, "3.10.0", new[] { "dd-sdk-android-rum", "dd-sdk-android-logs", "dd-sdk-android-ndk" });
            Assert.AreEqual(lines, gotLines);
        }

        [Test]
        public void MetricsFixFindsRumDeclarationWrittenByDependencyWrite()
        {
            string[] lines = GeneratedGradleFileBeforeDatadogDependencies.Split("\n");
            string[] withDeps = DatadogGradlePostProcessor.ApplyDatadogDependencies(
                lines, "3.10.0", new[] { "dd-sdk-android-rum", "dd-sdk-android-logs", "dd-sdk-android-ndk" });
            string[] gotLines = DatadogGradlePostProcessor.ApplyAndroidxMetricsCompatibilityFix(withDeps);
            string got = string.Join("\n", gotLines);
            Assert.AreEqual(GradleFileAsModifiedByDatadogGradlePostProcessor, got);
        }

        // Represents the post-generation unityLibrary/build.gradle as Unity emits it once EDM4U is gone: the
        // `implementation fileTree(...)` anchor and the `com.example` line are present, but no Datadog markers
        // or dependency declarations exist yet. Unity's own dependency placeholder substitution has already
        // happened by this point in the build, so no such placeholder is ever present in the file this hook
        // receives.
        private const string GeneratedGradleFileBeforeDatadogDependencies = @"apply plugin: 'com.android.library'


dependencies {
    implementation fileTree(dir: 'libs', include: ['*.jar'])
    implementation 'com.example:some-other-dependency:4.13.0'

    constraints {
         implementation(""org.jetbrains.kotlin:kotlin-stdlib-jdk8:1.8.0"") {
             because(""kotlin-stdlib-jdk8 is now a part of kotlin-stdlib"")
         }
    }

}

// Android Resolver Exclusions Start
android {
  namespace ""com.unity3d.player""
  packagingOptions {
      exclude ('/lib/armeabi/*' + '*')
      exclude ('/lib/mips/*' + '*')
      exclude ('/lib/mips64/*' + '*')
      exclude ('/lib/x86/*' + '*')
      exclude ('/lib/x86_64/*' + '*')
  }
}
// Android Resolver Exclusions End
android {
    ndkPath ""/Applications/Unity/Hub/Editor/2022.3.55f1/PlaybackEngines/AndroidPlayer/NDK""

    compileSdkVersion 35
    buildToolsVersion '34.0.0'

    compileOptions {
        sourceCompatibility JavaVersion.VERSION_11
        targetCompatibility JavaVersion.VERSION_11
    }

    defaultConfig {
        minSdkVersion 24
        targetSdkVersion 35
        ndk {
            abiFilters 'armeabi-v7a', 'arm64-v8a'
        }
        versionCode 1
        versionName '1.0'
        consumerProguardFiles 'proguard-unity.txt'
    }

    lintOptions {
        abortOnError false
    }

    aaptOptions {
        noCompress = ['.unity3d', '.ress', '.resource', '.obb', '.bundle', '.unityexp'] + unityStreamingAssets.tokenize(', ')
        ignoreAssetsPattern = ""!.svn:!.git:!.ds_store:!*.scc:!CVS:!thumbs.db:!picasa.ini:!*~""
    }

    packagingOptions {
        doNotStrip '*/armeabi-v7a/*.so'
        doNotStrip '*/arm64-v8a/*.so'
        jniLibs {
            useLegacyPackaging true
        }
    }
}
";

        // GeneratedGradleFileBeforeDatadogDependencies after ApplyDatadogDependencies has written the three
        // dd-sdk-android implementation lines (in rum/logs/ndk order, matching AndroidDependencyVersion.json's
        // artifacts array) immediately after the `implementation fileTree(...)` anchor.
        private const string GradleFileWithDatadogDependencies = @"apply plugin: 'com.android.library'


dependencies {
    implementation fileTree(dir: 'libs', include: ['*.jar'])
// Datadog Dependencies Start
    implementation 'com.datadoghq:dd-sdk-android-rum:3.10.0'
    implementation 'com.datadoghq:dd-sdk-android-logs:3.10.0'
    implementation 'com.datadoghq:dd-sdk-android-ndk:3.10.0'
// Datadog Dependencies End
    implementation 'com.example:some-other-dependency:4.13.0'

    constraints {
         implementation(""org.jetbrains.kotlin:kotlin-stdlib-jdk8:1.8.0"") {
             because(""kotlin-stdlib-jdk8 is now a part of kotlin-stdlib"")
         }
    }

}

// Android Resolver Exclusions Start
android {
  namespace ""com.unity3d.player""
  packagingOptions {
      exclude ('/lib/armeabi/*' + '*')
      exclude ('/lib/mips/*' + '*')
      exclude ('/lib/mips64/*' + '*')
      exclude ('/lib/x86/*' + '*')
      exclude ('/lib/x86_64/*' + '*')
  }
}
// Android Resolver Exclusions End
android {
    ndkPath ""/Applications/Unity/Hub/Editor/2022.3.55f1/PlaybackEngines/AndroidPlayer/NDK""

    compileSdkVersion 35
    buildToolsVersion '34.0.0'

    compileOptions {
        sourceCompatibility JavaVersion.VERSION_11
        targetCompatibility JavaVersion.VERSION_11
    }

    defaultConfig {
        minSdkVersion 24
        targetSdkVersion 35
        ndk {
            abiFilters 'armeabi-v7a', 'arm64-v8a'
        }
        versionCode 1
        versionName '1.0'
        consumerProguardFiles 'proguard-unity.txt'
    }

    lintOptions {
        abortOnError false
    }

    aaptOptions {
        noCompress = ['.unity3d', '.ress', '.resource', '.obb', '.bundle', '.unityexp'] + unityStreamingAssets.tokenize(', ')
        ignoreAssetsPattern = ""!.svn:!.git:!.ds_store:!*.scc:!CVS:!thumbs.db:!picasa.ini:!*~""
    }

    packagingOptions {
        doNotStrip '*/armeabi-v7a/*.so'
        doNotStrip '*/arm64-v8a/*.so'
        jniLibs {
            useLegacyPackaging true
        }
    }
}
";

        private const string GradleFileAsModifiedByDatadogGradlePostProcessor = @"apply plugin: 'com.android.library'


dependencies {
    implementation fileTree(dir: 'libs', include: ['*.jar'])
// Datadog Dependencies Start
    implementation('com.datadoghq:dd-sdk-android-rum:3.10.0') {
        // DatadogGradlePostProcessor: exclude the dependency on androidx.metrics:metrics-performance:1.0.0-beta02
        // Version beta02 requires Android Gradle plugin 8.6.0+, which is not supported on Unity 2022 and older
        exclude group: 'androidx.metrics', module: 'metrics-performance'
    }
    // DatadogGradlePostProcessor: Explicitly require version beta01 of the same dependency, as it works with AGP 7
    implementation 'androidx.metrics:metrics-performance:1.0.0-beta01'
    implementation 'com.datadoghq:dd-sdk-android-logs:3.10.0'
    implementation 'com.datadoghq:dd-sdk-android-ndk:3.10.0'
// Datadog Dependencies End
    implementation 'com.example:some-other-dependency:4.13.0'

    constraints {
         implementation(""org.jetbrains.kotlin:kotlin-stdlib-jdk8:1.8.0"") {
             because(""kotlin-stdlib-jdk8 is now a part of kotlin-stdlib"")
         }
    }

}

// Android Resolver Exclusions Start
android {
  namespace ""com.unity3d.player""
  packagingOptions {
      exclude ('/lib/armeabi/*' + '*')
      exclude ('/lib/mips/*' + '*')
      exclude ('/lib/mips64/*' + '*')
      exclude ('/lib/x86/*' + '*')
      exclude ('/lib/x86_64/*' + '*')
  }
}
// Android Resolver Exclusions End
android {
    ndkPath ""/Applications/Unity/Hub/Editor/2022.3.55f1/PlaybackEngines/AndroidPlayer/NDK""

    compileSdkVersion 35
    buildToolsVersion '34.0.0'

    compileOptions {
        sourceCompatibility JavaVersion.VERSION_11
        targetCompatibility JavaVersion.VERSION_11
    }

    defaultConfig {
        minSdkVersion 24
        targetSdkVersion 35
        ndk {
            abiFilters 'armeabi-v7a', 'arm64-v8a'
        }
        versionCode 1
        versionName '1.0'
        consumerProguardFiles 'proguard-unity.txt'
    }

    lintOptions {
        abortOnError false
    }

    aaptOptions {
        noCompress = ['.unity3d', '.ress', '.resource', '.obb', '.bundle', '.unityexp'] + unityStreamingAssets.tokenize(', ')
        ignoreAssetsPattern = ""!.svn:!.git:!.ds_store:!*.scc:!CVS:!thumbs.db:!picasa.ini:!*~""
    }

    packagingOptions {
        doNotStrip '*/armeabi-v7a/*.so'
        doNotStrip '*/arm64-v8a/*.so'
        jniLibs {
            useLegacyPackaging true
        }
    }
}
";

        [Test]
        public void AddsSubprojectsCoreForceToRootBuildGradle()
        {
            string[] lines = RootGradleFileAsGenerated.Split("\n");
            string[] gotLines = DatadogGradlePostProcessor.ApplyAndroidxCoreCompatibilityFix(lines);
            string got = string.Join("\n", gotLines);
            Assert.AreEqual(RootGradleFileWithCoreForce, got);
        }

        [Test]
        public void CoreFixHasNoEffectWhenRunAgain()
        {
            string[] lines = RootGradleFileWithCoreForce.Split("\n");
            string[] gotLines = DatadogGradlePostProcessor.ApplyAndroidxCoreCompatibilityFix(lines);
            Assert.AreEqual(lines, gotLines);
        }

        private const string RootGradleFileAsGenerated = @"buildscript {
    repositories {
        mavenLocal()
        mavenCentral()
    }
}

plugins {
    id 'com.android.application' version '7.4.2' apply false
    id 'com.android.library' version '7.4.2' apply false
    id 'org.jetbrains.kotlin.android' version '1.8.22' apply false
}

task clean(type: Delete) {
    delete rootProject.buildDir
}";

        private const string RootGradleFileWithCoreForce = @"buildscript {
    repositories {
        mavenLocal()
        mavenCentral()
    }
}

plugins {
    id 'com.android.application' version '7.4.2' apply false
    id 'com.android.library' version '7.4.2' apply false
    id 'org.jetbrains.kotlin.android' version '1.8.22' apply false
}

task clean(type: Delete) {
    delete rootProject.buildDir
}

// DatadogGradlePostProcessor: Force AGP 7-compatible androidx.core versions
// androidx.core 1.15.0+ ships Java 21 bytecode that D8 in AGP 7.x cannot process
subprojects {
    configurations.all {
        resolutionStrategy {
            force 'androidx.core:core:1.13.1'
            force 'androidx.core:core-ktx:1.13.1'
        }
    }
}";

        private const string GradleFileWithoutDatadogDependencies = @"apply plugin: 'com.android.library'

dependencies {
    implementation fileTree(dir: 'libs', include: ['*.jar'])
    implementation 'com.example:some-other-dependency:4.13.0'

    constraints {
         implementation(""org.jetbrains.kotlin:kotlin-stdlib-jdk8:1.8.0"") {
             because(""kotlin-stdlib-jdk8 is now a part of kotlin-stdlib"")
         }
    }

}

android {
    ndkPath ""/Applications/Unity/Hub/Editor/2022.3.55f1/PlaybackEngines/AndroidPlayer/NDK""

    compileSdkVersion 35
    buildToolsVersion '34.0.0'

    compileOptions {
        sourceCompatibility JavaVersion.VERSION_11
        targetCompatibility JavaVersion.VERSION_11
    }
}
";
    }
}
