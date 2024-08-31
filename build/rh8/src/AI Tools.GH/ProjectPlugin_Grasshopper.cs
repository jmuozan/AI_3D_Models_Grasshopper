using System;
using SD = System.Drawing;

using Rhino;
using Grasshopper.Kernel;

namespace RhinoCodePlatform.Rhino3D.Projects.Plugin.GH
{
  public sealed class AssemblyInfo : GH_AssemblyInfo
  {
    public override Guid Id { get; } = new Guid("58e29195-cc45-4a0d-b4e4-ca7163fa9660");

    public override string AssemblyName { get; } = "AI Tools.GH";
    public override string AssemblyVersion { get; } = "0.1.22353.9010";
    public override string AssemblyDescription { get; } = "AI generated Breps inside Grasshopper";
    public override string AuthorName { get; } = "Jorge Muñoz Zanón";
    public override string AuthorContact { get; } = "jmuozan@gmail.com";
    public override GH_LibraryLicense AssemblyLicense { get; } = GH_LibraryLicense.unset;
    public override SD.Bitmap AssemblyIcon { get; } = ProjectComponentPlugin.PluginIcon;
  }

  public class ProjectComponentPlugin : GH_AssemblyPriority
  {
    public static SD.Bitmap PluginIcon { get; }
    public static SD.Bitmap PluginCategoryIcon { get; }

    static readonly Guid s_rhinocode = new Guid("c9cba87a-23ce-4f15-a918-97645c05cde7");
    static readonly Type s_invokeContextType = default;
    static readonly dynamic s_projectServer = default;
    static readonly object s_project = default;

    static readonly Guid s_projectId = new Guid("58e29195-cc45-4a0d-b4e4-ca7163fa9660");
    static readonly string s_projectData = "ewogICJpZCI6ICI1OGUyOTE5NS1jYzQ1LTRhMGQtYjRlNC1jYTcxNjNmYTk2NjAiLAogICJpZGVudGl0eSI6IHsKICAgICJuYW1lIjogIkFJIFRvb2xzIiwKICAgICJ2ZXJzaW9uIjogIjAuMS1iZXRhIiwKICAgICJwdWJsaXNoZXIiOiB7CiAgICAgICJlbWFpbCI6ICJqbXVvemFuQGdtYWlsLmNvbSIsCiAgICAgICJuYW1lIjogIkpvcmdlIE11XHUwMEYxb3ogWmFuXHUwMEYzbiIsCiAgICAgICJjb3VudHJ5IjogIlNwYWluIiwKICAgICAgInVybCI6ICJodHRwczovL2ptdW96YW4uZ2l0aHViLmlvL2pvcmdlbXVueW96ei5naXRodWIuaW8vIgogICAgfSwKICAgICJkZXNjcmlwdGlvbiI6ICJBSSBnZW5lcmF0ZWQgQnJlcHMgaW5zaWRlIEdyYXNzaG9wcGVyIiwKICAgICJjb3B5cmlnaHQiOiAiQ29weXJpZ2h0IFx1MDBBOSAyMDI0IEpvcmdlIE11XHUwMEYxb3ogWmFuXHUwMEYzbiIsCiAgICAibGljZW5zZSI6ICJNSVQiLAogICAgInVybCI6ICJodHRwczovL2dpdGh1Yi5jb20vam11b3phbi9BSV8zRF9Nb2RlbHNfR3Jhc3Nob3BwZXIiCiAgfSwKICAic2V0dGluZ3MiOiB7CiAgICAiYnVpbGRQYXRoIjogImZpbGU6Ly8vVXNlcnMvam9yZ2VtdXlvL0Rlc2t0b3AvU1RVRkYvQUlfM0RfTW9kZWxzX0dyYXNzaG9wcGVyL2J1aWxkL3JoOCIsCiAgICAiYnVpbGRUYXJnZXQiOiB7CiAgICAgICJhcHBOYW1lIjogIlJoaW5vM0QiLAogICAgICAiYXBwVmVyc2lvbiI6IHsKICAgICAgICAibWFqb3IiOiA4CiAgICAgIH0sCiAgICAgICJ0aXRsZSI6ICJSaGlubzNEICg4LiopIiwKICAgICAgInNsdWciOiAicmg4IgogICAgfSwKICAgICJwdWJsaXNoVGFyZ2V0IjogewogICAgICAidGl0bGUiOiAiTWNOZWVsIFlhayBTZXJ2ZXIiCiAgICB9CiAgfSwKICAiY29kZXMiOiBbXQp9";
    static readonly string _iconData = "[[ASSEMBLY-ICON]]";

    static ProjectComponentPlugin()
    {
      s_projectServer = ProjectInterop.GetProjectServer();
      if (s_projectServer is null)
      {
        RhinoApp.WriteLine($"Error loading Grasshopper plugin. Missing Rhino3D platform");
        return;
      }

      // get project
      dynamic dctx = ProjectInterop.CreateInvokeContext();
      dctx.Inputs["projectAssembly"] = typeof(ProjectComponentPlugin).Assembly;
      dctx.Inputs["projectId"] = s_projectId;
      dctx.Inputs["projectData"] = s_projectData;

      object project = default;
      if (s_projectServer.TryInvoke("plugins/v1/deserialize", dctx)
            && dctx.Outputs.TryGet("project", out project))
      {
        // server reports errors
        s_project = project;
      }

      // get icons
      if (!_iconData.Contains("ASSEMBLY-ICON"))
      {
        dynamic ictx = ProjectInterop.CreateInvokeContext();
        ictx.Inputs["iconData"] = _iconData;
        SD.Bitmap icon = default;
        if (s_projectServer.TryInvoke("plugins/v1/icon/gh/assembly", ictx)
              && ictx.Outputs.TryGet("icon", out icon))
        {
          // server reports errors
          PluginIcon = icon;
        }

        if (s_projectServer.TryInvoke("plugins/v1/icon/gh/category", ictx)
              && ictx.Outputs.TryGet("icon", out icon))
        {
          // server reports errors
          PluginCategoryIcon = icon;
        }
      }
    }

    public override GH_LoadingInstruction PriorityLoad()
    {
      Grasshopper.Instances.ComponentServer.AddCategorySymbolName("AI Tools", "AI Tools"[0]);

      if (PluginCategoryIcon != null)
        Grasshopper.Instances.ComponentServer.AddCategoryIcon("AI Tools", PluginCategoryIcon);

      return GH_LoadingInstruction.Proceed;
    }

    public static bool TryCreateScript(GH_Component ghcomponent, string serialized, out object script)
    {
      script = default;

      if (s_projectServer is null) return false;

      dynamic dctx = ProjectInterop.CreateInvokeContext();
      dctx.Inputs["component"] = ghcomponent;
      dctx.Inputs["project"] = s_project;
      dctx.Inputs["scriptData"] = serialized;

      if (s_projectServer.TryInvoke("plugins/v1/gh/deserialize", dctx))
      {
        return dctx.Outputs.TryGet("script", out script);
      }

      return false;
    }

    public static bool TryCreateScriptIcon(object script, out SD.Bitmap icon)
    {
      icon = default;

      if (s_projectServer is null) return false;

      dynamic ictx = ProjectInterop.CreateInvokeContext();
      ictx.Inputs["script"] = script;

      if (s_projectServer.TryInvoke("plugins/v1/icon/gh/script", ictx))
      {
        // server reports errors
        return ictx.Outputs.TryGet("icon", out icon);
      }

      return false;
    }

    public static void DisposeScript(GH_Component ghcomponent, object script)
    {
      if (script is null)
        return;

      dynamic dctx = ProjectInterop.CreateInvokeContext();
      dctx.Inputs["component"] = ghcomponent;
      dctx.Inputs["project"] = s_project;
      dctx.Inputs["script"] = script;

      if (!s_projectServer.TryInvoke("plugins/v1/gh/dispose", dctx))
        throw new Exception("Error disposing Grasshopper script component");
    }
  }
}
