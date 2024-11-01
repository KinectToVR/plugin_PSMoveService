// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Drawing;
using System.IO;
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
[ExportMetadata("Version", "1.0.0.1")]
[ExportMetadata("Website", "https://github.com/KinectToVR/plugin_PSMoveService")]
[ExportMetadata("DependencyLink", "https://github.com/Timocop/PSMoveServiceEx/releases")]
[ExportMetadata("DependencyInstaller", typeof(CoreInstaller))]
[ExportMetadata("CoreSetupData", typeof(SetupData))]
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

    public DirectoryInfo ConfigFolder => new(Path.Join(Environment.GetFolderPath(
        Environment.SpecialFolder.ApplicationData), "PSMoveService"));

    public bool IsPositionFilterBlockingEnabled => true;
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
        try
        {
            // Prepare joint placeholders if this is the 1st time loading
            if (!PluginLoaded && ConfigFolder.Exists)
                lock (Host.UpdateThreadLock)
                {
                    TrackedJoints.Clear(); // Clear the list to make some space

                    // Add all valid PSMoves present in the configuration directory
                    TrackedJoints.AddRange(ConfigFolder.EnumerateFiles("??_??_??_??_??_??.json")
                        .Select(x => new TrackedJoint
                            { Name = Path.GetFileNameWithoutExtension(x.Name), Role = TrackedJointType.JointManual }));

                    // Add all valid Virtual-s present in the configuration directory
                    TrackedJoints.AddRange(ConfigFolder.EnumerateFiles("VirtualController_*.json")
                        .Select(x => new TrackedJoint
                            { Name = Path.GetFileNameWithoutExtension(x.Name), Role = TrackedJointType.JointManual }));
                }
        }
        catch (Exception e)
        {
            Host?.Log($"Error appending default joints! Message: {e.Message}", LogSeverity.Error);
        }

        if (!PluginLoaded && !TrackedJoints.Any()) // Append a placeholder joint if still empty
            TrackedJoints.Add(new TrackedJoint { Name = "INVALID", Role = TrackedJointType.JointManual });

        // Prepare the settings interface
        LedToggleSwitch = new ToggleSwitch
        {
            OnContent = "", OffContent = "",
            IsOn = Host!.PluginSettings.GetSetting("LedDim", false),
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

                // Update input actions (using extensions defined below)
                // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
                switch (controller.m_Info.m_ControllerType)
                {
                    case Constants.PSMControllerType.PSMController_Move:
                        controller.m_Info.m_PSMoveState.UpdateActions(Host);
                        break;
                    case Constants.PSMControllerType.PSMController_Navi:
                        controller.m_Info.m_PSNaviState.UpdateActions(Host);
                        break;
                    case Constants.PSMControllerType.PSMController_DualShock4:
                        controller.m_Info.m_PSDualShock4State.UpdateActions(Host);
                        break;
                }
            });
        }
        catch (Exception e)
        {
            Host?.Log($"Couldn't update PSM Service! {e.Message}");
            Host?.Log("Checking the service status again...");

            // Re-compute the status
            DeviceStatus = IsInitialized
                ? Service.IsConnected()
                    ? TrackedJoints.First().Name != "INVALID"
                        ? 0 // Everything's fine!
                        : 1 // Connected, no joints
                    : 2 // Not even connected
                : 3; // Not initialized (yet?)

            // Request a quick refresh of the status
            Host?.RefreshStatusInterface();
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
                        Name = $"{controller.m_Info.m_ControllerSerial}" +
                               (controller.m_Info.m_ControllerType is Constants.PSMControllerType.PSMController_Virtual
                                   ? controller.m_Info
                                       .m_ControllerId // Is the controller is virtual, also append its ID
                                   : ""), // If this controller is valid, its serial should be the bluetooth-mac address
                        Role = TrackedJointType.JointManual,
                        SupportedInputActions = controller.m_Info.m_ControllerType switch
                        {
                            Constants.PSMControllerType.PSMController_Move =>
                                controller.m_Info.m_PSMoveState.GetActions(),
                            Constants.PSMControllerType.PSMController_Navi =>
                                controller.m_Info.m_PSNaviState.GetActions(),
                            Constants.PSMControllerType.PSMController_DualShock4 =>
                                controller.m_Info.m_PSDualShock4State.GetActions(),

                            Constants.PSMControllerType.PSMController_Virtual => [],
                            Constants.PSMControllerType.PSMController_None => [],
                            _ => []
                        }
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
                controller.m_Info.m_Pose.m_Position.x / 100f,
                controller.m_Info.m_Pose.m_Position.y / 100f,
                controller.m_Info.m_Pose.m_Position.z / 100f)
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

    public static void AddRange<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
    {
        items.ToList().ForEach(collection.Add);
    }

    public static void UpdateActions(this Controllers.Info.PSMoveState state, IAmethystHost host)
    {
        try
        {
            state.GetButtonStates()
                .Select((IKeyInputAction Action, object Data) (x) => (Action: new KeyInputAction<bool>
                {
                    Name = x.Key,
                    Guid = x.Key
                }, Data: x.Value)).Concat(state.GetAnalogStates()
                    .Select((IKeyInputAction Action, object Data) (x) => (Action: new KeyInputAction<double>
                    {
                        Name = x.Key,
                        Guid = x.Key
                    }, Data: x.Value))).ToList().ForEach(x => host.ReceiveKeyInput(x.Action, x.Data));
        }
        catch (Exception e)
        {
            host?.Log(e);
        }
    }

    public static void UpdateActions(this Controllers.Info.PSDualShock4State state, IAmethystHost host)
    {
        try
        {
            state.GetButtonStates()
                .Select((IKeyInputAction Action, object Data) (x) => (Action: new KeyInputAction<bool>
                {
                    Name = x.Key,
                    Guid = x.Key
                }, Data: x.Value)).Concat(state.GetAnalogStates()
                    .Select((IKeyInputAction Action, object Data) (x) => (Action: new KeyInputAction<double>
                    {
                        Name = x.Key,
                        Guid = x.Key
                    }, Data: x.Value))).ToList().ForEach(x => host.ReceiveKeyInput(x.Action, x.Data));
        }
        catch (Exception e)
        {
            host?.Log(e);
        }
    }

    public static void UpdateActions(this Controllers.Info.PSNaviState state, IAmethystHost host)
    {
        try
        {
            state.GetButtonStates()
                .Select((IKeyInputAction Action, object Data) (x) => (Action: new KeyInputAction<bool>
                {
                    Name = x.Key,
                    Guid = x.Key
                }, Data: x.Value)).ToList().ForEach(x => host.ReceiveKeyInput(x.Action, x.Data));
        }
        catch (Exception e)
        {
            host?.Log(e);
        }
    }

    public static SortedSet<IKeyInputAction> GetActions(this Controllers.Info.PSMoveState state)
    {
        return new SortedSet<IKeyInputAction>(state.GetButtonStates()
            .Select(IKeyInputAction (x) => new KeyInputAction<bool>
            {
                Name = x.Key, Guid = x.Key
            }).Concat(state.GetAnalogStates().Select(IKeyInputAction (x) => new KeyInputAction<double>
            {
                Name = x.Key,
                Guid = x.Key
            })).ToList());
    }

    public static SortedSet<IKeyInputAction> GetActions(this Controllers.Info.PSDualShock4State state)
    {
        return new SortedSet<IKeyInputAction>(state.GetButtonStates()
            .Select(IKeyInputAction (x) => new KeyInputAction<bool>
            {
                Name = x.Key,
                Guid = x.Key
            }).Concat(state.GetAnalogStates().Select(IKeyInputAction (x) => new KeyInputAction<double>
            {
                Name = x.Key,
                Guid = x.Key
            })).ToList());
    }

    public static SortedSet<IKeyInputAction> GetActions(this Controllers.Info.PSNaviState state)
    {
        return new SortedSet<IKeyInputAction>(state.GetButtonStates()
            .Select(IKeyInputAction (x) => new KeyInputAction<bool>
            {
                Name = x.Key,
                Guid = x.Key
            }).ToList());
    }

    public static Dictionary<string, bool> GetButtonStates(this Controllers.Info.PSMoveState state)
    {
        return new Dictionary<string, bool>
        {
            { "Triangle", state?.m_TriangleButton == Constants.PSMButtonState.PSMButtonState_PRESSED },
            { "Circle", state?.m_CircleButton == Constants.PSMButtonState.PSMButtonState_PRESSED },
            { "Cross", state?.m_CrossButton == Constants.PSMButtonState.PSMButtonState_PRESSED },
            { "Square", state?.m_SquareButton == Constants.PSMButtonState.PSMButtonState_PRESSED },
            { "Select", state?.m_SelectButton == Constants.PSMButtonState.PSMButtonState_PRESSED },
            { "Start", state?.m_StartButton == Constants.PSMButtonState.PSMButtonState_PRESSED },
            { "PS", state?.m_PSButton == Constants.PSMButtonState.PSMButtonState_PRESSED },
            { "Move", state?.m_MoveButton == Constants.PSMButtonState.PSMButtonState_PRESSED },
            { "Trigger", state?.m_TriggerButton == Constants.PSMButtonState.PSMButtonState_PRESSED }
        };
    }

    public static Dictionary<string, double> GetAnalogStates(this Controllers.Info.PSMoveState state)
    {
        return new Dictionary<string, double>
        {
            { "Trigger (Linear)", (state?.m_TriggerValue ?? 0.0) / 255.0 }
        };
    }

    public static Dictionary<string, bool> GetButtonStates(this Controllers.Info.PSDualShock4State state)
    {
        return new Dictionary<string, bool>
        {
            { "D-Pad Up", state?.m_DPadUpButton == Constants.PSMButtonState.PSMButtonState_PRESSED },
            { "D-Pad Down", state?.m_DPadDownButton == Constants.PSMButtonState.PSMButtonState_PRESSED },
            { "D-Pad Left", state?.m_DPadLeftButton == Constants.PSMButtonState.PSMButtonState_PRESSED },
            { "D-Pad Right", state?.m_DPadRightButton == Constants.PSMButtonState.PSMButtonState_PRESSED },
            { "Square", state?.m_SquareButton == Constants.PSMButtonState.PSMButtonState_PRESSED },
            { "Cross", state?.m_CrossButton == Constants.PSMButtonState.PSMButtonState_PRESSED },
            { "Circle", state?.m_CircleButton == Constants.PSMButtonState.PSMButtonState_PRESSED },
            { "Triangle", state?.m_TriangleButton == Constants.PSMButtonState.PSMButtonState_PRESSED },
            { "L1", state?.m_L1Button == Constants.PSMButtonState.PSMButtonState_PRESSED },
            { "R1", state?.m_R1Button == Constants.PSMButtonState.PSMButtonState_PRESSED },
            { "L2", state?.m_L2Button == Constants.PSMButtonState.PSMButtonState_PRESSED },
            { "R2", state?.m_R2Button == Constants.PSMButtonState.PSMButtonState_PRESSED },
            { "L3", state?.m_L3Button == Constants.PSMButtonState.PSMButtonState_PRESSED },
            { "R3", state?.m_R3Button == Constants.PSMButtonState.PSMButtonState_PRESSED },
            { "Share", state?.m_ShareButton == Constants.PSMButtonState.PSMButtonState_PRESSED },
            { "Options", state?.m_OptionsButton == Constants.PSMButtonState.PSMButtonState_PRESSED },
            { "PS", state?.m_PSButton == Constants.PSMButtonState.PSMButtonState_PRESSED },
            { "Trackpad", state?.m_TrackPadButton == Constants.PSMButtonState.PSMButtonState_PRESSED }
        };
    }

    public static Dictionary<string, double> GetAnalogStates(this Controllers.Info.PSDualShock4State state)
    {
        return new Dictionary<string, double>
        {
            { "Left X", Math.Clamp(state?.m_LeftAnalogX ?? 0.0, 0.0, 1.0) },
            { "Left Y", Math.Clamp(state?.m_LeftAnalogY ?? 0.0, 0.0, 1.0) },
            { "Right X", Math.Clamp(state?.m_RightAnalogX ?? 0.0, 0.0, 1.0) },
            { "Right Y", Math.Clamp(state?.m_RightAnalogY ?? 0.0, 0.0, 1.0) },
            { "Left Trigger", Math.Clamp(state?.m_LeftTriggerValue ?? 0.0, 0.0, 1.0) },
            { "Right Trigger", Math.Clamp(state?.m_RightTriggerValue ?? 0.0, 0.0, 1.0) }
        };
    }

    public static Dictionary<string, bool> GetButtonStates(this Controllers.Info.PSNaviState state)
    {
        return new Dictionary<string, bool>
        {
            { "D-Pad Up", state?.m_DPadUpButton == Constants.PSMButtonState.PSMButtonState_PRESSED },
            { "D-Pad Down", state?.m_DPadDownButton == Constants.PSMButtonState.PSMButtonState_PRESSED },
            { "D-Pad Left", state?.m_DPadLeftButton == Constants.PSMButtonState.PSMButtonState_PRESSED },
            { "D-Pad Right", state?.m_DPadRightButton == Constants.PSMButtonState.PSMButtonState_PRESSED },
            { "Cross", state?.m_CrossButton == Constants.PSMButtonState.PSMButtonState_PRESSED },
            { "Circle", state?.m_CircleButton == Constants.PSMButtonState.PSMButtonState_PRESSED },
            { "L1", state?.m_L1Button == Constants.PSMButtonState.PSMButtonState_PRESSED },
            { "L2", state?.m_L2Button == Constants.PSMButtonState.PSMButtonState_PRESSED },
            { "L3", state?.m_L3Button == Constants.PSMButtonState.PSMButtonState_PRESSED },
            { "PS", state?.m_PSButton == Constants.PSMButtonState.PSMButtonState_PRESSED }
        };
    }
}