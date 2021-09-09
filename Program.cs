using System;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace EtwHostServiceMsiCleanUp
{
  internal static class Program
  {
    private static readonly char[] ourHexSymbols = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F' };

    private static string ToMsiPresentation(this Guid guid)
    {
      var bytes = guid.ToByteArray();
      var builder = new StringBuilder(bytes.Length * 2);
      foreach (var b in bytes)
        builder.Append(ourHexSymbols[b & 0xF]).Append(ourHexSymbols[b >> 4]);
      return builder.ToString();
    }

    private static void CleanUpIf(RegistryKey key, Func<RegistryKey, string, bool> func)
    {
      if (key == null) throw new ArgumentNullException(nameof(key));
      if (func == null) throw new ArgumentNullException(nameof(func));
      foreach (var subKeyName in key.GetSubKeyNames())
      {
        bool toDelete;
        using (var subKey = key.OpenSubKey(subKeyName))
          toDelete = func(subKey, subKeyName);
        if (toDelete)
        {
          Console.WriteLine("  {0}", subKeyName);
          key.DeleteSubKeyTree(subKeyName);
        }
      }
    }

    private static int Main()
    {
      try
      {
        var version = ((AssemblyInformationalVersionAttribute)typeof(Program).Assembly.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false).First()).InformationalVersion;
        Console.WriteLine("ETW Host Service MSI CleanUp Tool v{0} Mikhail Pilin", version);

        var masterUpgradeCode = new Guid("25CB994F-CDCF-421B-9156-76528AAFC0E1").ToMsiPresentation();
        var serviceOldComponent = new Guid("1D4FAF23-64A9-4B77-AACE-1AB92385D09C").ToMsiPresentation();

        if (masterUpgradeCode != "F499BC52FCDCB12419656725A8FA0C1E")
          throw new Exception("Failed");
        if (serviceOldComponent != "32FAF4D19A4677B4AAECA19B32580DC9")
          throw new Exception("Failed");

        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
          throw new Exception("Run under the elevated administrator");

        using var installerKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer");

        Console.WriteLine("UpgradeCodes:");
        using (var upgradeCodesKey = installerKey!.OpenSubKey(@"UpgradeCodes", true))
          CleanUpIf(upgradeCodesKey, (_, subKeyName) => subKeyName == masterUpgradeCode);

        using (var userSidsKey = installerKey!.OpenSubKey("UserData"))
          foreach (var userSidName in userSidsKey!.GetSubKeyNames())
          {
            using var userSidKey = userSidsKey!.OpenSubKey(userSidName);

            Console.WriteLine("Components {0}:", userSidName);
            using (var componentsKey = userSidKey!.OpenSubKey("Components", true))
              if (componentsKey != null)
                CleanUpIf(componentsKey, (subKey, subKeyName) =>
                  {
                    if (subKeyName == serviceOldComponent)
                      return true;

                    foreach (var valueName in subKey!.GetValueNames())
                      if (subKey!.GetValue(valueName) is string str && str.Contains("ETW Host"))
                        return true;
                    return false;
                  });

            Console.WriteLine("Products {0}:", userSidName);
            using (var productsKey = userSidKey!.OpenSubKey("Products", true))
              if (productsKey != null)
                CleanUpIf(productsKey, (subKey, _) =>
                  {
                    using var propertySubKey = subKey.OpenSubKey("InstallProperties");
                    return propertySubKey?.GetValue("DisplayName") is string str && (str.StartsWith("JetBrains ETW Host Service") || str.Equals("JetBrains ETW Service"));
                  });
          }

        Console.WriteLine("Done");
        return 0;
      }
      catch (Exception e)
      {
        Console.Error.WriteLine("ERROR: {0}", e);
        return 1;
      }
    }
  }
}