// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Amethyst.Plugins.Contract;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static PSMoveServiceExCAPI.PSMoveServiceExCAPI;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace plugin_PSMoveService;

[Export(typeof(ITrackingDevice))]
[ExportMetadata("Name", "PSMove Service")]
[ExportMetadata("Guid", "K2VRTEAM-AME2-APII-DVCE-DVCEPSMOVEEX")]
[ExportMetadata("Publisher", "K2VR Team")]
[ExportMetadata("Version", "1.0.0.0")]
[ExportMetadata("Website", "https://github.com/KinectToVR/plugin_PSMoveService")]
public class PsMoveService : ITrackingDevice
{
    [Import(typeof(IAmethystHost))] private IAmethystHost Host { get; set; }
    private Service Service { get; } = new("127.0.0.1");

    private bool PluginLoaded { get; set; }
    private Page InterfaceRoot { get; set; }
    private ToggleSwitch LedToggleSwitch { get; set; }
    private TextBlock LedTextBlock { get; set; }
    private TextBlock LedHeaderTextBlock { get; set; }
    private List<Controllers> PsControllers { get; set; } = new();

    public bool IsPositionFilterBlockingEnabled => false;
    public bool IsPhysicsOverrideEnabled => true;
    public bool IsSelfUpdateEnabled => false;
    public bool IsFlipSupported => false;
    public bool IsAppOrientationSupported => false;
    public bool IsSettingsDaemonSupported => DeviceStatus == 0;
    public object SettingsInterfaceRoot => InterfaceRoot;

    public bool IsInitialized => Service.IsInitialized();
    public bool IsSkeletonTracked { get; private set; }
    public int DeviceStatus { get; private set; } = 3;

    public ObservableCollection<TrackedJoint> TrackedJoints { get; } = new()
    {
        new TrackedJoint { Name = "INVALID", Role = TrackedJointType.JointManual }
    };

    public string DeviceStatusString => PluginLoaded
        ? DeviceStatus switch
        {
            0 => Host.RequestLocalizedString("/Plugins/PSMS/Statuses/Success"),
            1 => Host.RequestLocalizedString("/Plugins/PSMS/Statuses/NoJoints"),
            2 => Host.RequestLocalizedString("/Plugins/PSMS/Statuses/NotConnected"),
            3 => Host.RequestLocalizedString("/Plugins/PSMS/Statuses/NotRunning"),
            _ => $"Undefined: {DeviceStatus}\nE_UNDEFINED\nSomething weird has happened, though we can't tell what."
        }
        : $"Undefined: {DeviceStatus}\nE_UNDEFINED\nSomething weird has happened, though we can't tell what.";

    public Uri ErrorDocsUri => new($"https://docs.k2vr.tech/{Host?.DocsLanguageCode ?? "en"}/psmove/troubleshooting/");

    public void OnLoad()
    {
        LedToggleSwitch = new ToggleSwitch
        {
            OnContent = "", OffContent = "",
            IsOn = Host.PluginSettings.GetSetting("LedDim", false),
            Margin = new Thickness { Left = 5, Top = 3, Right = 3, Bottom = 3 },
            VerticalAlignment = VerticalAlignment.Center
        };

        LedToggleSwitch.Toggled += (sender, _) =>
        {
            DimControllers((sender as ToggleSwitch)?.IsOn ?? false); // Try to dim the controller
            Host.PluginSettings.SetSetting("LedDim", (sender as ToggleSwitch)?.IsOn ?? false);
            Host.PlayAppSound((sender as ToggleSwitch)?.IsOn ?? false ? SoundType.ToggleOn : SoundType.ToggleOff);
        };

        LedHeaderTextBlock = new TextBlock
        {
            Text = Host.RequestLocalizedString("/Plugins/PSMS/Settings/Contents/Dim"),
            Margin = new Thickness { Left = 3, Top = 5, Right = 5, Bottom = 3 },
            VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.WrapWholeWords
        };

        LedTextBlock = new TextBlock
        {
            Text = Host.RequestLocalizedString("/Plugins/PSMS/Settings/Labels/Dim"),
            Margin = new Thickness { Left = 3, Top = 3, Right = 3, Bottom = 8 },
            VerticalAlignment = VerticalAlignment.Center
        };

        InterfaceRoot = new Page
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Children =
                {
                    LedHeaderTextBlock,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children = { LedTextBlock, LedToggleSwitch }
                    }
                }
            }
        };

        PluginLoaded = true;
    }

    public void Initialize()
    {
        // Try connecting to the service
        try
        {
            // Initialize the API
            if (!Service.IsConnected())
            {
                Service.Disconnect();
                Service.Connect();
            }

            // Rebuild controllers
            RefreshControllerList();

            // Re-compute the status
            DeviceStatus = IsInitialized
                ? Service.IsConnected()
                    ? TrackedJoints.First().Name != "INVALID"
                        ? 0 // Everything's fine!
                        : 1 // Connected, no joints
                    : 2 // Not even connected
                : 3; // Not initialized (yet?)

            // Refresh inside amethyst
            Host?.RefreshStatusInterface();
        }
        catch (Exception e)
        {
            Host?.Log($"Couldn't connect to the PSM Service! {e.Message}");
        }
    }

    public void Shutdown()
    {
        // Try disconnection from the service
        try
        {
            lock (Host!.UpdateThreadLock)
            {
                // Close streams
                PsControllers.ForEach(x => x?.Disconnect());

                // Shutdown the API
                Service.Disconnect();
            }

            // Re-compute the status
            DeviceStatus = IsInitialized
                ? Service.IsConnected()
                    ? TrackedJoints.First().Name != "INVALID"
                        ? 0 // Everything's fine!
                        : 1 // Connected, no joints
                    : 2 // Not even connected
                : 3; // Not initialized (yet?)

            // Refresh inside amethyst
            Host?.RefreshStatusInterface();
        }
        catch (Exception e)
        {
            Host?.Log($"Couldn't disconnect from the PSM Service! {e.Message}");
        }
    }

    public void Update()
    {
        if (!PluginLoaded || !IsInitialized ||
            DeviceStatus != 0) return; // Sanity check

        // Try connecting to the service
        try
        {
            Service.Update(); // Update the service
            IsSkeletonTracked = false; // Stub check

            // Refresh on changes
            if (Service.HasConnectionStatusChanged()) RefreshControllerList();

            // Refresh all controllers/all
            using var jointEnumerator = TrackedJoints.GetEnumerator();
            PsControllers.ForEach(controller =>
            {
                // Refresh the controller
                controller.Refresh(Controllers.Info.RefreshFlags.RefreshType_All);

                // Ove to the next controller list entry
                if (!jointEnumerator.MoveNext() ||
                    jointEnumerator.Current is null) return;

                // Note we're all fine
                IsSkeletonTracked = true;

                // Copy pose data from the controller
                jointEnumerator.Current.Position = controller.PoseVector();
                jointEnumerator.Current.Orientation = controller.OrientationQuaternion();

                //// Copy physics data from the controller
                //jointEnumerator.Current.Velocity = controller.PoseVelocity();
                //jointEnumerator.Current.Acceleration = controller.PoseAcceleration();
                //jointEnumerator.Current.AngularVelocity = controller.PoseAngularVelocity();
                //jointEnumerator.Current.AngularAcceleration = controller.PoseAngularAcceleration();

                // Parse/copy the tracking state
                jointEnumerator.Current.TrackingState = controller.IsControllerStable()
                    ? TrackedJointState.StateTracked // Tracking is all fine!
                    : TrackedJointState.StateInferred; // Kinda unstable...?
            });
        }
        catch (Exception e)
        {
            Host?.Log($"Couldn't update PSM Service! {e.Message}");
        }
    }

    public void SignalJoint(int jointId)
    {
        if (!PluginLoaded || !IsInitialized ||
            DeviceStatus != 0) return; // Sanity check

        // Try buzzing the selected controller
        Task.Run(() =>
        {
            try
            {
                // Try setting controller rumble
                PsControllers.ElementAt(jointId).SetControllerRumble(
                    Constants.PSMControllerRumbleChannel.PSMControllerRumbleChannel_All, 1f);

                Task.Delay(100); // Sleep a bit

                // Try resetting controller rumble
                PsControllers.ElementAt(jointId).SetControllerRumble(
                    Constants.PSMControllerRumbleChannel.PSMControllerRumbleChannel_All, 0f);
            }
            catch (Exception e)
            {
                Host?.Log($"Couldn't re/set controller [{jointId}] rumble! {e.Message}");
            }
        });
    }

    public void DimControllers(bool enabled)
    {
        if (!PluginLoaded || !IsInitialized ||
            DeviceStatus != 0) return; // Sanity check

        // Try buzzing the selected controller
        Task.Run(() =>
        {
            try
            {
                // Try setting controller rumble
                PsControllers.ForEach(x => x.SetControllerLEDOverrideColor(
                    enabled ? Color.FromArgb(1, 1, 1) : Color.FromArgb(0, 0, 0)));
            }
            catch (Exception e)
            {
                Host?.Log($"Couldn't re/set controllers' colors! {e.Message}");
            }
        });
    }

    private void RefreshControllerList()
    {
        // Try polling controllers and starting their streams
        try
        {
            Host?.Log("Locking the update thread...");
            lock (Host!.UpdateThreadLock)
            {
                Host?.Log("Emptying the tracked joints list...");
                TrackedJoints.Clear(); // Delete literally everything

                Host?.Log("Closing all controller streams...");
                PsControllers.ForEach(x => x?.Disconnect());

                Host?.Log("Searching for tracked controllers...");
                PsControllers = Controllers.GetControllerList().ToList();
                if (!PsControllers.Any())
                {
                    Host?.Log("Didn't find any valid controllers within the PSM Service!");
                    TrackedJoints.Add(new TrackedJoint
                    {
                        Name = "INVALID", Role = TrackedJointType.JointManual
                    });
                    goto refresh; // Refresh everything after the change
                }

                // Add all the available PSMoves
                foreach (var controller in PsControllers)
                {
                    Host?.Log($"Found a valid, usable controller: {controller}");

                    Host?.Log("Setting up controller streams...");
                    controller.m_DataStreamFlags =
                        Constants.PSMStreamFlags.PSMStreamFlags_includeCalibratedSensorData |
                        Constants.PSMStreamFlags.PSMStreamFlags_includePhysicsData |
                        Constants.PSMStreamFlags.PSMStreamFlags_includePositionData |
                        Constants.PSMStreamFlags.PSMStreamFlags_includeRawSensorData |
                        Constants.PSMStreamFlags.PSMStreamFlags_includeRawTrackerData;

                    Host?.Log("Enabling controller streams...");
                    controller.m_Listening = true;
                    controller.m_DataStreamEnabled = true;

                    Host?.Log("Adding the new controller to the controller list...");
                    TrackedJoints.Add(new TrackedJoint
                    {
                        Name = $"{controller.m_Info.m_ControllerSerial} {controller.m_Info.m_ControllerId}",
                        Role = TrackedJointType.JointManual
                    });
                }
            }

            // Jump straight to the function end
            refresh:

            // Refresh everything after the change
            Host?.Log("Refreshing the UI...");
            Host?.RefreshStatusInterface();
        }
        catch (Exception e)
        {
            Host?.Log($"Couldn't connect to the PSM Service! {e.Message}");
        }
    }
}

public static class DataExtensions
{
    public static Vector3 PoseVelocity(this Controllers controller)
    {
        return controller.m_Info.IsPhysicsValid()
            ? new Vector3(
                controller.m_Info.m_Physics.m_LinearVelocityCmPerSec.x / 100f,
                controller.m_Info.m_Physics.m_LinearVelocityCmPerSec.y / 100f,
                controller.m_Info.m_Physics.m_LinearVelocityCmPerSec.z / 100f)
            : Vector3.Zero; // Nothing if invalid
    }

    public static Vector3 PoseAcceleration(this Controllers controller)
    {
        return controller.m_Info.IsPhysicsValid()
            ? new Vector3(
                controller.m_Info.m_Physics.m_LinearAccelerationCmPerSecSqr.x / 100f,
                controller.m_Info.m_Physics.m_LinearAccelerationCmPerSecSqr.y / 100f,
                controller.m_Info.m_Physics.m_LinearAccelerationCmPerSecSqr.z / 100f)
            : Vector3.Zero; // Nothing if invalid
    }

    public static Vector3 PoseAngularVelocity(this Controllers controller)
    {
        return controller.m_Info.IsPhysicsValid()
            ? new Vector3(
                controller.m_Info.m_Physics.m_AngularVelocityRadPerSec.x / 100f,
                controller.m_Info.m_Physics.m_AngularVelocityRadPerSec.y / 100f,
                controller.m_Info.m_Physics.m_AngularVelocityRadPerSec.z / 100f)
            : Vector3.Zero; // Nothing if invalid
    }

    public static Vector3 PoseAngularAcceleration(this Controllers controller)
    {
        return controller.m_Info.IsPhysicsValid()
            ? new Vector3(
                controller.m_Info.m_Physics.m_AngularAccelerationRadPerSecSqr.x / 100f,
                controller.m_Info.m_Physics.m_AngularAccelerationRadPerSecSqr.y / 100f,
                controller.m_Info.m_Physics.m_AngularAccelerationRadPerSecSqr.z / 100f)
            : Vector3.Zero; // Nothing if invalid
    }

    public static Vector3 PoseVector(this Controllers controller)
    {
        return controller.m_Info.IsPoseValid()
            ? new Vector3(
                controller.m_Info.m_Pose.m_Position.x,
                controller.m_Info.m_Pose.m_Position.y,
                controller.m_Info.m_Pose.m_Position.z)
            : Vector3.Zero; // Nothing if invalid
    }

    public static Quaternion OrientationQuaternion(this Controllers controller)
    {
        return controller.m_Info.IsPoseValid()
            ? new Quaternion(
                controller.m_Info.m_Pose.m_Orientation.x,
                controller.m_Info.m_Pose.m_Orientation.y,
                controller.m_Info.m_Pose.m_Orientation.z,
                controller.m_Info.m_Pose.m_Orientation.w)
            : Quaternion.Identity; // Nothing if invalid
    }
}