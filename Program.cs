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

    private static T NotNull<T>(this T t) where T : class
    {
      if (t == null)
        throw new NullReferenceException();
      return t;
    }

    private static string GetPad(int pad) => new(' ', 2 * pad);

    private static void CleanUpIf(int pad, RegistryKey key, Func<RegistryKey, string, bool> func)
    {
      if (key == null) throw new ArgumentNullException(nameof(key));
      if (func == null) throw new ArgumentNullException(nameof(func));
      foreach (var subKeyName in key.GetSubKeyNames())
      {
        bool toDelete;
        using (var subKey = key.OpenSubKey(subKeyName).NotNull())
          toDelete = func(subKey, subKeyName);
        if (toDelete)
        {
          Console.WriteLine("{0}{1}", GetPad(pad), subKeyName);
          key.DeleteSubKeyTree(subKeyName);
        }
      }
    }

    private static void CleanUpUpgradeCodes(int pad, RegistryKey key)
    {
      if (key == null) throw new ArgumentNullException(nameof(key));

      var masterUpgradeCode = new Guid("25CB994F-CDCF-421B-9156-76528AAFC0E1").ToMsiPresentation();
      if (masterUpgradeCode != "F499BC52FCDCB12419656725A8FA0C1E")
        throw new Exception("Failed");

      using var upgradeCodesKey = key.OpenSubKey(@"UpgradeCodes", true);
      if (upgradeCodesKey == null)
        return;

      Console.WriteLine("{0}UpgradeCodes:", GetPad(pad));
      CleanUpIf(pad + 1, upgradeCodesKey, (_, subKeyName) => subKeyName == masterUpgradeCode);
    }

    private static void CleanUpComponents(int pad, RegistryKey key)
    {
      if (key == null) throw new ArgumentNullException(nameof(key));

      var serviceOldComponent = new Guid("1D4FAF23-64A9-4B77-AACE-1AB92385D09C").ToMsiPresentation();
      if (serviceOldComponent != "32FAF4D19A4677B4AAECA19B32580DC9")
        throw new Exception("Failed");

      using var componentsKey = key.OpenSubKey("Components", true);
      if (componentsKey == null)
        return;

      Console.WriteLine("{0}Components:", GetPad(pad));
      CleanUpIf(pad + 1, componentsKey, (subKey, subKeyName) =>
        {
          if (subKeyName == serviceOldComponent)
            return true;

          foreach (var valueName in subKey.GetValueNames())
            if (subKey.GetValue(valueName) is string str && str.Contains("ETW Host"))
              return true;
          return false;
        });
    }

    private static void CleanUpProducts(int pad, RegistryKey key)
    {
      if (key == null) throw new ArgumentNullException(nameof(key));

      using var productsKey = key.OpenSubKey("Products", true);
      if (productsKey == null)
        return;

      Console.WriteLine("{0}Products:", GetPad(pad));
      CleanUpIf(pad + 1, productsKey, (subKey, _) =>
        {
          using var propertySubKey = subKey.OpenSubKey("InstallProperties");
          return propertySubKey?.GetValue("DisplayName") is string str && (str.StartsWith("JetBrains ETW Host Service") || str.Equals("JetBrains ETW Service"));
        });
    }

    private static void CleanUpClassProducts(int pad, RegistryKey key)
    {
      if (key == null)
        throw new ArgumentNullException(nameof(key));

      using var productsKey = key.OpenSubKey("Products", true);
      if (productsKey == null)
        return;

      Console.WriteLine("{0}ClassProducts:", GetPad(pad));
      CleanUpIf(pad + 1, productsKey, (subKey, _) => subKey?.GetValue("ProductName") is string str && (str.StartsWith("JetBrains ETW Host Service") || str.Equals("JetBrains ETW Service")));
    }

    private static int Main()
    {
      try
      {
        var version = ((AssemblyInformationalVersionAttribute)typeof(Program).Assembly.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false).First()).InformationalVersion;
        Console.WriteLine("ETW Host Service MSI CleanUp Tool v{0} Mikhail Pilin", version);

        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
          throw new Exception("Run under the elevated administrator");

        Console.WriteLine(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer:");
        using (var installerKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer").NotNull())
        {
          CleanUpUpgradeCodes(1, installerKey);
          using (var userSidsKey = installerKey.OpenSubKey("UserData").NotNull())
            foreach (var userSidName in userSidsKey.GetSubKeyNames())
            {
              Console.WriteLine("{0}{1}:", GetPad(1), userSidName);
              using var userSidKey = userSidsKey.OpenSubKey(userSidName).NotNull();
              CleanUpComponents(2, userSidKey);
              CleanUpProducts(2, userSidKey);
            }
        }

        Console.WriteLine(@"SOFTWARE\Classes\Installer:");
        using (var installerClassesKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes\Installer").NotNull())
        {
          CleanUpUpgradeCodes(1, installerClassesKey);
          CleanUpClassProducts(1, installerClassesKey);
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