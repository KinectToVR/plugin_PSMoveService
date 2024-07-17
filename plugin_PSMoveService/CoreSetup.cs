using System;
using System.Collections.Generic;
using Amethyst.Plugins.Contract;
using Microsoft.UI.Xaml.Controls;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RestSharp;
using System.IO.Compression;
using Windows.Storage;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Diagnostics;
using System.Reflection;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace plugin_PSMoveService;

internal class SetupData : ICoreSetupData
{
    public object PluginIcon => new PathIcon
    {
        Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry),
            "M55.38,5.07c.07-1.44-.75-1.95-1.94-1.95-7.67,0-15.33,0-23,0H30a1.36,1.36,0,0,0-1.43,1.4q0,2.43,0,4.86a1.38,1.38,0,0,0,1.54,1.45h6.7c-.59,2.69-.13,4.62,1.6,6a2,2,0,0,1,.88,2,1.61,1.61,0,0,0,0,.38c.09.53-.17.64-.66.63-1.54,0-3.09,0-4.64,0-.55,0-.8.11-.75.72a19.78,19.78,0,0,1,0,2.31c0,.5.12.66.64.65,2.09,0,4.18,0,6.27,0,3.3,0,6.6,0,9.91,0,.53,0,.8-.08.76-.7a19.78,19.78,0,0,1,0-2.31c0-.5-.14-.68-.66-.67-1.26,0-2.53,0-3.79,0-1.71,0-1.7,0-1.71-1.67a1.12,1.12,0,0,1,.52-1,5,5,0,0,0,2.17-3.75,22.62,22.62,0,0,0-.09-2.57h6.51a1.43,1.43,0,0,0,1.62-1.61C55.37,7.85,55.3,6.46,55.38,5.07ZM42,15.79a3,3,0,1,1,2.93-3A3.08,3.08,0,0,1,42,15.79ZM19.53,1.14C17.3,1,15.92,2.51,15.64,5.43a3.11,3.11,0,0,1-.25-.2c-.71-.65-.7-.64-1.31.1-.86,1-1.69,2.07-2.59,3.06a83.16,83.16,0,0,1-9.78,9.08,3.61,3.61,0,0,0-1.4,3.76,3.71,3.71,0,0,0,2.82,3c1.59.43,3.09-.25,4.26-1.8A78.23,78.23,0,0,1,18.82,10c.76-.65.76-.65.07-1.37,0,0,0-.12-.05-.19C21.35,8.38,23,7,23,4.8A3.66,3.66,0,0,0,19.53,1.14Zm-8.76,13.3a1,1,0,1,1,1.06-1A1,1,0,0,1,10.77,14.44Zm2.68-2.67a1.23,1.23,0,0,1-.87-.47,1.19,1.19,0,0,1,.06-1.06,9.6,9.6,0,0,1,1.6-1.65,1.15,1.15,0,0,1,1-.08c.28.11.44.49.55.63C15.79,9.94,14.06,11.82,13.45,11.77Z")
    };

    public string GroupName => string.Empty;
    public Type PluginType => typeof(ITrackingDevice);
}

internal class CoreInstaller : IDependencyInstaller
{
	public IDependencyInstaller.ILocalizationHost Host { get; set; }

	public List<IFix> ListFixes() => new();

    public List<IDependency> ListDependencies()
    {
        return new List<IDependency>
        {
            new EyeDrivers
            {
                Host = Host,
                Name = Host?.RequestLocalizedString("/Plugins/Kinect360/Dependencies/Eye/Name") ??
                       "PSEye Drivers"
            },
            new PsmService
            {
                Host = Host,
                Name = Host?.RequestLocalizedString("/Plugins/Kinect360/Dependencies/PSMS/Name") ??
                       "PSMoveService"
            },
            new VdManager
            {
                Host = Host,
                Name = Host?.RequestLocalizedString("/Plugins/Kinect360/Dependencies/VDM/Name") ??
                       "Virtual Device Manager"
            }
        };
    }
}

internal class PsmService : IDependency
{
    public IDependencyInstaller.ILocalizationHost Host { get; set; }

    public async Task<bool> Install(IProgress<InstallationProgress> progress, CancellationToken cancellationToken)
    {
        // Amethyst will handle this exception for us anyway
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var sourceZip = Path.Join(Directory.GetParent(Assembly.GetExecutingAssembly().Location)!.FullName,
                "Assets", "Resources", "Dependencies", "PSMoveService.zip");

            var outputDirectory = (await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                "Vendor", CreationCollisionOption.OpenIfExists)).Path;

            if (File.Exists(sourceZip))
            {
                try
                {
                    // Extract the toolset
                    ZipFile.ExtractToDirectory(sourceZip, outputDirectory, true);
                }
                catch (Exception e)
                {
                    progress.Report(new InstallationProgress
                    {
                        IsIndeterminate = true,
                        StageTitle =
                            (Host?.RequestLocalizedString("/Plugins/PSMS/Stages/Exceptions/Service/Extraction") ??
                             "PSMove Service extraction failed! Exception: {0}").Replace("{0}", e.Message)
                    });

                    return false;
                }

                return true;
            }
        }
        catch (Exception e)
        {
            progress.Report(new InstallationProgress
            {
                IsIndeterminate = true,
                StageTitle = (Host?.RequestLocalizedString("/Plugins/PSMS/Stages/Exceptions/Service/Installation") ??
                              "PSMove Service installation failed! Exception: {0}").Replace("{0}", e.Message)
            });
            return false;
        }

        return false;
    }

    public string Name { get; set; }
    public bool IsMandatory => false;
    public bool IsInstalled => false;
    public string InstallerEula => string.Empty;
}

internal class VdManager : IDependency
{
    public IDependencyInstaller.ILocalizationHost Host { get; set; }

    private const string DownloadUrl =
        "https://github.com/Timocop/PSMoveServiceEx-Virtual-Device-Manager/releases/download/v8.2/PSMSVirtualDeviceManager.zip";

    private string TemporaryFolderName { get; } = Guid.NewGuid().ToString().ToUpper();

    private async Task<StorageFolder> GetTempDirectory()
    {
        return await ApplicationData.Current.TemporaryFolder.CreateFolderAsync(
            TemporaryFolderName, CreationCollisionOption.OpenIfExists);
    }

    public async Task<bool> Install(IProgress<InstallationProgress> progress, CancellationToken cancellationToken)
    {
        // Amethyst will handle this exception for us anyway
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var sourceZip = Path.Join(Directory.GetParent(Assembly.GetExecutingAssembly().Location)!.FullName,
                "Assets", "Resources", "Dependencies", "PSMSVirtualDeviceManager.zip");

            var outputDirectory = (await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                "Vendor", CreationCollisionOption.OpenIfExists)).Path;

            if (File.Exists(sourceZip))
            {
                try
                {
                    // Extract the toolset
                    ZipFile.ExtractToDirectory(sourceZip, outputDirectory, true);
                }
                catch (Exception e)
                {
                    progress.Report(new InstallationProgress
                    {
                        IsIndeterminate = true,
                        StageTitle =
                            (Host?.RequestLocalizedString("/Plugins/PSMS/Stages/Exceptions/VDM/Extraction") ??
                             "PSMS-VDM extraction failed! Exception: {0}").Replace("{0}", e.Message)
                    });

                    return false;
                }

                // Set up settings
                var vdmConfigPath =
                    Path.GetFullPath(Path.Combine(outputDirectory, "PSMSVirtualDeviceManager", "settings.ini"));
                var psmsPath = Path.Combine(ApplicationData.Current.LocalFolder.Path, "Vendor", "PSMoveService",
                    "PSMoveService.exe");
                if (!File.Exists(vdmConfigPath) && File.Exists(psmsPath))
                {
                    var contents = $"[Settings]\r\nPSMoveServiceLocation={psmsPath}";
                    await File.WriteAllTextAsync(vdmConfigPath, contents, cancellationToken);
                }

                // Create start menu shortcuts
                var link = (IShellLink)new ShellLink();

                link.SetDescription("Launch VDM");
                link.SetPath(Path.Combine(outputDirectory, "PSMSVirtualDeviceManager", "PSMSVirtualDeviceManager.exe"));

                ((IPersistFile)link).Save(Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonStartMenu), "PSMS Virtual Device Manager.lnk"), false);

                return true;
            }
        }
        catch (Exception e)
        {
            progress.Report(new InstallationProgress
            {
                IsIndeterminate = true,
                StageTitle = (Host?.RequestLocalizedString("/Plugins/PSMS/Stages/Exceptions/VDM/Installation") ??
                              "PSMS-VDM installation failed! Exception: {0}").Replace("{0}", e.Message)
            });
            return false;
        }

        return false;
    }

    public string Name { get; set; }
    public bool IsMandatory => false;
    public bool IsInstalled => false;
    public string InstallerEula => string.Empty;
}

internal class EyeDrivers : IDependency
{
    public IDependencyInstaller.ILocalizationHost Host { get; set; }

    private const int VENDOR_ID = 0x1415;
    private const int PRODUCT_ID = 0x2000;

    private string TemporaryFolderName { get; } = Guid.NewGuid().ToString().ToUpper();

    private async Task<StorageFolder> GetTempDirectory()
    {
        return await ApplicationData.Current.TemporaryFolder.CreateFolderAsync(
            TemporaryFolderName, CreationCollisionOption.OpenIfExists);
    }

    public async Task<bool> Install(IProgress<InstallationProgress> progress, CancellationToken cancellationToken)
    {
        // Amethyst will handle this exception for us anyway
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var sourceZip = Path.Join(Directory.GetParent(Assembly.GetExecutingAssembly().Location)!.FullName,
                "Assets", "Resources", "Dependencies", "PSMSVirtualDeviceManager.zip");

            var outputDirectory = (await GetTempDirectory()).Path;

            if (File.Exists(sourceZip))
            {
                try
                {
                    // Extract the toolset
                    ZipFile.ExtractToDirectory(sourceZip, outputDirectory, true);
                }
                catch (Exception e)
                {
                    progress.Report(new InstallationProgress
                    {
                        IsIndeterminate = true,
                        StageTitle =
                            (Host?.RequestLocalizedString("/Plugins/PSMS/Stages/Exceptions/Eye/Extraction") ??
                             "Archive extraction failed! Exception: {0}").Replace("{0}", e.Message)
                    });

                    return false;
                }

                // wdi-simple.exe -n "USB Playstation Eye Camera" -f "USB Playstation Eye Camera.inf" -m "Nam Tai E&E Products Ltd. or OmniVision Technologies, Inc." -v "5141" -p "8192" -t 1

                progress.Report(new InstallationProgress
                {
                    IsIndeterminate = true,
                    StageTitle = Host?.RequestLocalizedString("/Plugins/PSMS/Stages/Installing/Eye") ??
                                 "Setting up PSEye Drivers"
                });

                var libusbDir =
                    Path.GetFullPath(Path.Combine(outputDirectory, "PSMSVirtualDeviceManager", "libusb_driver"));

                // Try using this helper executable provided with PSMS VDM to install the PSEye driver
                var installDriversProc = Process.Start(new ProcessStartInfo()
                {
                    FileName = Path.Combine(libusbDir, "wdi-simple.exe"),
                    WorkingDirectory = libusbDir,
                    Arguments =
                        $"--timeout 300000 -n \"USB Playstation Eye Camera\" -f \"USB Playstation Eye Camera.inf\" -m \"Nam Tai E&E Products Ltd. or OmniVision Technologies, Inc.\" -v \"{VENDOR_ID}\" -p \"{PRODUCT_ID}\" -t 1",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    CreateNoWindow = true,
                    UseShellExecute = false
                });

                await installDriversProc!.WaitForExitAsync(cancellationToken);
                if (installDriversProc.ExitCode == 0)
                {
                    progress.Report(new InstallationProgress
                    {
                        IsIndeterminate = true,
                        StageTitle = Host?.RequestLocalizedString("/Plugins/PSMS/Stages/Installation/Eye/Success") ??
                                     "PSEye Drivers installed successfully!"
                    });

                    return true;
                }

                // Bad exit code, no special handling for exit codes yet
                progress.Report(new InstallationProgress
                {
                    IsIndeterminate = true,
                    StageTitle =
                        (Host?.RequestLocalizedString("/Plugins/PSMS/Stages/Installation/Eye/Fail") ??
                         "PSEye Drivers installation failed! Exit code: {0}")
                        .Replace("{0}", installDriversProc.ExitCode.ToString())
                });

                return false;
            }
        }
        catch (Exception e)
        {
            progress.Report(new InstallationProgress
            {
                IsIndeterminate = true,
                StageTitle = (Host?.RequestLocalizedString("/Plugins/PSMS/Stages/Exceptions/Service/Installation") ??
                              "PSEye Drivers installation failed! Exception: {0}").Replace("{0}", e.Message)
            });
            return false;
        }

        return false;
    }

    public string Name { get; set; }
    public bool IsMandatory => false;
    public bool IsInstalled => false;
    public string InstallerEula => string.Empty;
}

[ComImport]
[Guid("00021401-0000-0000-C000-000000000046")]
internal class ShellLink
{
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("000214F9-0000-0000-C000-000000000046")]
internal interface IShellLink
{
    void GetPath([Out] [MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, out IntPtr pfd,
        int fFlags);

    void GetIDList(out IntPtr ppidl);
    void SetIDList(IntPtr pidl);
    void GetDescription([Out] [MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
    void GetWorkingDirectory([Out] [MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
    void GetArguments([Out] [MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
    void GetHotkey(out short pwHotkey);
    void SetHotkey(short wHotkey);
    void GetShowCmd(out int piShowCmd);
    void SetShowCmd(int iShowCmd);

    void GetIconLocation([Out] [MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath,
        out int piIcon);

    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
    void Resolve(IntPtr hwnd, int fFlags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}