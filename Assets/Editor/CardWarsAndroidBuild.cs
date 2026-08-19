using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class CardWarsAndroidBuild
{
	private const string OutputPath = "Builds/Android/CardWars-ARM64-Unlocked-LAN.apk";

	public static void BuildApk()
	{
		ConfigureExternalTools();
		EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
		EditorUserBuildSettings.buildAppBundle = false;
		EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;

		PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
		PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
		PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel22;
		// SDK 36 keeps LAN access compatible on Android 17 through the legacy
		// INTERNET grant. Targeting SDK 37 requires ACCESS_LOCAL_NETWORK at runtime.
		PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)36;
		PlayerSettings.Android.useCustomKeystore = false;
		PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
		PlayerSettings.allowedAutorotateToLandscapeLeft = true;
		PlayerSettings.allowedAutorotateToLandscapeRight = true;
		PlayerSettings.allowedAutorotateToPortrait = false;
		PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

		string[] scenes = EditorBuildSettings.scenes
			.Where(scene => scene.enabled)
			.Select(scene => scene.path)
			.ToArray();

		if (scenes.Length == 0)
		{
			throw new InvalidOperationException("No enabled scenes were found in EditorBuildSettings.");
		}

		string absoluteOutputPath = Path.GetFullPath(OutputPath);
		Directory.CreateDirectory(Path.GetDirectoryName(absoluteOutputPath));

		BuildPlayerOptions options = new BuildPlayerOptions
		{
			scenes = scenes,
			locationPathName = absoluteOutputPath,
			target = BuildTarget.Android,
			options = BuildOptions.None
		};

		Debug.Log("Building Card Wars ARM64 APK at: " + absoluteOutputPath);
		BuildReport report = BuildPipeline.BuildPlayer(options);
		BuildSummary summary = report.summary;

		if (summary.result != BuildResult.Succeeded)
		{
			throw new Exception(string.Format(
				"Android build failed: {0} errors, {1} warnings, result {2}",
				summary.totalErrors,
				summary.totalWarnings,
				summary.result));
		}

		Debug.Log(string.Format(
			"Android build succeeded: {0} bytes in {1}",
			summary.totalSize,
			summary.totalTime));
	}

	private static void ConfigureExternalTools()
	{
		string sdkPath = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
		string ndkPath = Environment.GetEnvironmentVariable("ANDROID_NDK_ROOT");
		string jdkPath = Environment.GetEnvironmentVariable("JAVA_HOME");

		if (string.IsNullOrEmpty(sdkPath) || !Directory.Exists(sdkPath))
		{
			throw new DirectoryNotFoundException("ANDROID_SDK_ROOT is missing or invalid: " + sdkPath);
		}
		if (string.IsNullOrEmpty(ndkPath) || !Directory.Exists(ndkPath))
		{
			throw new DirectoryNotFoundException("ANDROID_NDK_ROOT is missing or invalid: " + ndkPath);
		}
		if (string.IsNullOrEmpty(jdkPath) || !Directory.Exists(jdkPath))
		{
			throw new DirectoryNotFoundException("JAVA_HOME is missing or invalid: " + jdkPath);
		}

		AndroidExternalToolsSettings.sdkRootPath = sdkPath;
		AndroidExternalToolsSettings.ndkRootPath = ndkPath;
		AndroidExternalToolsSettings.jdkRootPath = jdkPath;
		Console.WriteLine(string.Format(
			"Android tools configured: SDK={0}, NDK={1}, JDK={2}",
			AndroidExternalToolsSettings.sdkRootPath,
			AndroidExternalToolsSettings.ndkRootPath,
			AndroidExternalToolsSettings.jdkRootPath));
		Debug.Log(string.Format("Android tools: SDK={0}, NDK={1}, JDK={2}", sdkPath, ndkPath, jdkPath));
	}
}
