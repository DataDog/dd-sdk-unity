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
            string[] lines = GradleFileAsWrittenByEdm.Split("\n");
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
        public void HasNoEffectIfEdmSectionDoesNotExist()
        {
            string[] lines = GradleFileWithoutEdmDependencies.Split("\n");
            string[] gotLines = DatadogGradlePostProcessor.ApplyAndroidxMetricsCompatibilityFix(lines);
            Assert.AreEqual(lines, gotLines);
        }

        private const string GradleFileAsWrittenByEdm = @"apply plugin: 'com.android.library'


dependencies {
    implementation fileTree(dir: 'libs', include: ['*.jar'])
// Android Resolver Dependencies Start
    implementation 'com.datadoghq:dd-sdk-android-logs:2+' // Packages/com.datadoghq.unity/Editor/DatadogDependencies.xml:10
    implementation 'com.datadoghq:dd-sdk-android-ndk:2+' // Packages/com.datadoghq.unity/Editor/DatadogDependencies.xml:12
    implementation 'com.datadoghq:dd-sdk-android-rum:2+' // Packages/com.datadoghq.unity/Editor/DatadogDependencies.xml:14
    implementation 'com.example:some-other-dependency:4.13.0' // Packages/com.datadoghq.unity/Editor/DatadogDependencies.xml:16
// Android Resolver Dependencies End

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
// Android Resolver Dependencies Start
    implementation 'com.datadoghq:dd-sdk-android-logs:2+' // Packages/com.datadoghq.unity/Editor/DatadogDependencies.xml:10
    implementation 'com.datadoghq:dd-sdk-android-ndk:2+' // Packages/com.datadoghq.unity/Editor/DatadogDependencies.xml:12
    implementation('com.datadoghq:dd-sdk-android-rum:2+') { // Packages/com.datadoghq.unity/Editor/DatadogDependencies.xml:14
        // DatadogGradlePostProcessor: exclude the dependency on androidx.metrics:metrics-performance:1.0.0-beta02
        // Version beta02 requires Android Gradle plugin 8.6.0+, which is not supported on Unity 2022 and older
        exclude group: 'androidx.metrics', module: 'metrics-performance'
    }
    // DatadogGradlePostProcessor: Explicitly require version beta01 of the same dependency, as it works with AGP 7
    implementation 'androidx.metrics:metrics-performance:1.0.0-beta01'
    implementation 'com.example:some-other-dependency:4.13.0' // Packages/com.datadoghq.unity/Editor/DatadogDependencies.xml:16
// Android Resolver Dependencies End

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

        private const string GradleFileWithoutEdmDependencies = @"apply plugin: 'com.android.library'

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
