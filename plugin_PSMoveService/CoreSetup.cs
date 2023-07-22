using System;
using System.Collections.Generic;
using Amethyst.Plugins.Contract;
using Microsoft.UI.Xaml.Controls;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using RestSharp;
using System.IO.Compression;
using Windows.Storage;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Diagnostics;

namespace plugin_PSMoveService;

internal class SetupData : ICoreSetupData
{
    public object PluginIcon => new BitmapIcon
    {
        UriSource = new Uri(Path.Join(Directory.GetParent(
                Assembly.GetExecutingAssembly().Location)!.FullName,
            "Assets", "Resources", "icon.png"))
    };

    public string GroupName => string.Empty;
    public Type PluginType => typeof(ITrackingDevice);
}

internal class CoreInstaller : IDependencyInstaller
{
    public IDependencyInstaller.ILocalizationHost Host { get; set; }

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

    private const string DownloadUrl =
        "https://github.com/Timocop/PSMoveServiceEx/releases/download/v0.21/PSMoveService.zip";

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
            using var client = new RestClient();
            progress.Report(new InstallationProgress
            {
                IsIndeterminate = true,
                StageTitle = Host?.RequestLocalizedString("/Plugins/PSMS/Stages/Downloading/Service") ??
                             "Downloading PSMove Service"
            });

            // Create a stream reader using the received Installer Uri
            await using var stream =
                await client.ExecuteDownloadStreamAsync(DownloadUrl, new RestRequest());

            // Replace or create our installer file
            var installerFile = await (await GetTempDirectory()).CreateFileAsync(
                "PSMoveService-latest.zip", CreationCollisionOption.ReplaceExisting);

            // Create an output stream and push all the available data to it
            await using var fsInstallerFile = await installerFile.OpenStreamForWriteAsync();
            await stream.CopyToWithProgressAsync(fsInstallerFile, cancellationToken,
                innerProgress =>
                {
                    progress.Report(new InstallationProgress
                    {
                        IsIndeterminate = false,
                        OverallProgress = innerProgress / 18942046.0,
                        StageTitle = Host?.RequestLocalizedString("/Plugins/PSMS/Stages/Downloading/Service") ??
                                     "Downloading PSMove Service"
                    });
                }); // The runtime will do the rest for us

            // Close the file to unlock it
            fsInstallerFile.Close();

            var sourceZip = Path.GetFullPath(Path.Combine((await GetTempDirectory()).Path, "PSMoveService-latest.zip"));
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
            using var client = new RestClient();
            progress.Report(new InstallationProgress
            {
                IsIndeterminate = true,
                StageTitle = Host?.RequestLocalizedString("/Plugins/PSMS/Stages/Downloading/VDM") ??
                             "Downloading PSMS-VDManager"
            });

            // Create a stream reader using the received Installer Uri
            await using var stream =
                await client.ExecuteDownloadStreamAsync(DownloadUrl, new RestRequest());

            // Replace or create our installer file
            var installerFile = await (await GetTempDirectory()).CreateFileAsync(
                "PSMSVirtualDeviceManager.zip", CreationCollisionOption.ReplaceExisting);

            // Create an output stream and push all the available data to it
            await using var fsInstallerFile = await installerFile.OpenStreamForWriteAsync();
            await stream.CopyToWithProgressAsync(fsInstallerFile, cancellationToken,
                innerProgress =>
                {
                    progress.Report(new InstallationProgress
                    {
                        IsIndeterminate = false,
                        OverallProgress = innerProgress / 36897216.0,
                        StageTitle = Host?.RequestLocalizedString("/Plugins/PSMS/Stages/Downloading/VDM") ??
                                     "Downloading PSMS-VDManager"
                    });
                }); // The runtime will do the rest for us

            // Close the file to unlock it
            fsInstallerFile.Close();

            var sourceZip =
                Path.GetFullPath(Path.Combine((await GetTempDirectory()).Path, "PSMSVirtualDeviceManager.zip"));
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
                var vdmConfigPath = Path.GetFullPath(Path.Combine(outputDirectory, "PSMSVirtualDeviceManager", "settings.ini"));
                var psmsPath = Path.Combine(ApplicationData.Current.LocalFolder.Path, "Vendor", "PSMoveService", "PSMoveService.exe");
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
            using var client = new RestClient();
            progress.Report(new InstallationProgress
            {
                IsIndeterminate = true,
                StageTitle = Host?.RequestLocalizedString("/Plugins/PSMS/Stages/Downloading/Eye") ??
                             "Downloading PSEye Drivers"
            });

            // Create a stream reader using the received Installer Uri
            await using var stream =
                await client.ExecuteDownloadStreamAsync(DownloadUrl, new RestRequest());

            // Replace or create our installer file
            var installerFile = await (await GetTempDirectory()).CreateFileAsync(
                "PSMSVirtualDeviceManager.zip", CreationCollisionOption.ReplaceExisting);

            // Create an output stream and push all the available data to it
            await using var fsInstallerFile = await installerFile.OpenStreamForWriteAsync();
            await stream.CopyToWithProgressAsync(fsInstallerFile, cancellationToken,
                innerProgress =>
                {
                    progress.Report(new InstallationProgress
                    {
                        IsIndeterminate = false,
                        OverallProgress = innerProgress / 36897216.0,
                        StageTitle = Host?.RequestLocalizedString("/Plugins/PSMS/Stages/Downloading/Eye") ??
                                     "Downloading PSEye Drivers"
                    });
                }); // The runtime will do the rest for us

            // Close the file to unlock it
            fsInstallerFile.Close();

            var sourceZip =
                Path.GetFullPath(Path.Combine((await GetTempDirectory()).Path, "PSMSVirtualDeviceManager.zip"));
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

public static class RestExtensions
{
    public static Task<byte[]> ExecuteDownloadDataAsync(this RestClient client, string baseUrl, RestRequest request)
    {
        client.Options.BaseUrl = new Uri(baseUrl);
        return client.DownloadDataAsync(request);
    }

    public static Task<Stream> ExecuteDownloadStreamAsync(this RestClient client, string baseUrl, RestRequest request)
    {
        client.Options.BaseUrl = new Uri(baseUrl);
        return client.DownloadStreamAsync(request);
    }
}

public static class StreamExtensions
{
    public static async Task CopyToWithProgressAsync(this Stream source,
        Stream destination, CancellationToken cancellationToken,
        Action<long> progress = null, int bufferSize = 10240)
    {
        var buffer = new byte[bufferSize];
        var total = 0L;
        int amtRead;

        do
        {
            amtRead = 0;
            while (amtRead < bufferSize)
            {
                var numBytes = await source.ReadAsync(
                    buffer, amtRead, bufferSize - amtRead, cancellationToken);
                if (numBytes == 0) break;
                amtRead += numBytes;
            }

            total += amtRead;
            await destination.WriteAsync(buffer, 0, amtRead, cancellationToken);
            progress?.Invoke(total);
        } while (amtRead == bufferSize);
    }
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