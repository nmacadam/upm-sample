using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.VersionControl;
using UnityEditorInternal;
using UnityEngine;

public class PackageWizard : EditorWindow
{
    private string _manifestPath = string.Empty;
    
    private string _companyName = "Company";
    private string _packageName = "Package";
    
    private static string _showedPackageWizard = "PackageWizardEditor.showedWizard";

    [InitializeOnLoadMethod]
    private static void OnProjectLoadedInEditor()
    {
        SelectWizardAutomatically();
    }
	
    private static void SelectWizardAutomatically()
    {
        if (!SessionState.GetBool(_showedPackageWizard, false))
        {
            SelectPackageWizard();
            SessionState.SetBool(_showedPackageWizard, true);
        } 
    }
    
    [MenuItem("Window/Package Wizard")]
    static void SelectPackageWizard() 
    {
        GetWindow<PackageWizard>("Package Wizard").Show();
    }

    private void OnEnable()
    {
        // Set default manifest path
        _manifestPath = $"{GetProjectPath()}/Packages/com.company.package/package.json";
    }
    
    private void OnGUI()
    {
        DrawHeader();

        _manifestPath = FileField("Manifest Path", _manifestPath, "Select Package Manifest", "json");
        bool hasManifest = File.Exists(_manifestPath);
        if (!hasManifest)
        {
            EditorGUILayout.LabelField("Could not find a package manifest");
            return;
        }
        
        GUIStyle validFieldStyle = new GUIStyle(EditorStyles.textField);
        validFieldStyle.normal.textColor = Color.green;
        validFieldStyle.focused.textColor = Color.green;
        validFieldStyle.hover.textColor = Color.green;
        
        GUIStyle invalidFieldStyle = new GUIStyle(EditorStyles.textField);
        invalidFieldStyle.normal.textColor = Color.red;
        invalidFieldStyle.focused.textColor = Color.red;
        invalidFieldStyle.hover.textColor = Color.red;
        
        string domainCompanyName = _companyName.ToLower();
        string domainPackageName = _packageName.ToLower();

        bool isCompanyValid = IsNameValid(domainCompanyName);
        bool isPackageValid = IsNameValid(domainPackageName);

        _companyName = EditorGUILayout.TextField("Company Name", _companyName, isCompanyValid ? validFieldStyle : invalidFieldStyle);
        _packageName = EditorGUILayout.TextField("Package Name", _packageName, isPackageValid ? validFieldStyle : invalidFieldStyle);
        
        // This is pretty non-performant but it's a one-time use tool

        GUIStyle richTextLabel = new GUIStyle(GUI.skin.label) { richText = true };
        
        // Get original values for change previews
        string manifest = File.ReadAllText(_manifestPath);
        
        Regex manifestDomainNameRegex = new Regex("(?<=(\"name\"[:]\\s\"))([A-z._-]*)");
        var manifestDomainName = manifestDomainNameRegex.Match(manifest).Value;
        
        // Draw change previews
        EditorGUILayout.Space();
        
        DrawChangePreview(manifestDomainName, $"com.{domainCompanyName}.{domainPackageName}", richTextLabel);
        DrawAsmdefChanges(_packageName);

        // Disable if not following package domain name naming conventions
        // https://docs.unity3d.com/Manual/cus-naming.html
        EditorGUILayout.Space();
        GUI.enabled = isCompanyValid && isPackageValid;

        if (GUILayout.Button("Update Package Data"))
        {
            // Find all assembly definitions in project that aren't under Packages folder
            string[] guids = AssetDatabase.FindAssets("t:asmdef");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!IsSelectedPackage(path)) continue;

                var asmdef = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(path);

                string newName = asmdef.name.Remove(0, asmdef.name.IndexOf('.')).Insert(0, _packageName);

                string contents = File.ReadAllText(path);
                
                // Update assembly name to match new package name
                Regex nameRegex = new Regex("(?<=(\"name\"[:]\\s\"))([A-z]*)");
                contents = nameRegex.Replace(contents, _packageName);
                
                // Update root namespace to match new package name
                Regex namespaceRegex = new Regex("(?<=(\"rootNamespace\"[:]\\s\"))([A-z]*)");
                contents = namespaceRegex.Replace(contents, _packageName);
                
                // Write changes to assembly definition
                File.WriteAllText(path, contents);
                
                // Rename assembly definition to match new package name (and to trigger reimport)
                AssetDatabase.RenameAsset(path, newName);
            }
            
            // Rename package domain name to match new name 
            var match = manifestDomainNameRegex.Match(manifest);
            manifest = manifest.Remove(match.Index, match.Length)
                .Insert(match.Index, $"com.{domainCompanyName}.{domainPackageName}");
            
            // Write updated manifest contents
            File.WriteAllText(_manifestPath, manifest);
            
            // Rename package directory
            Directory.Move(GetPackageDirectoryPath(), GetPackageDirectoryPath().Replace(GetPackageFolderName(), $"com.{domainCompanyName}.{domainPackageName}"));
            
            // Update manifest path
            _manifestPath = $"{GetProjectPath()}/Packages/com.{domainCompanyName}.{domainPackageName}/package.json";
            
            // Refresh Asset Database so package gets re-imported immediately
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        }
        
        GUI.enabled = true;
    }

    #region GUI

    private void DrawHeader()
    {
        float headerHeight = 45;
        var rect = EditorGUILayout.GetControlRect(GUILayout.Height(headerHeight));
        GUIContent icon = EditorGUIUtility.IconContent("Package Manager@2x");
        EditorGUI.LabelField(rect, new GUIContent("Package Wizard", icon.image), new GUIStyle("LargeLabel") { alignment = TextAnchor.MiddleCenter, fontSize = 24, fontStyle = FontStyle.Bold });
    }

    private void DrawAsmdefChanges(string packageName)
    {
        string[] guids = AssetDatabase.FindAssets("t:asmdef");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!IsSelectedPackage(path)) continue;

            var asmdef = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(path);

            string newFileName = asmdef.name.Remove(0, asmdef.name.IndexOf('.')).Insert(0, packageName);

            DrawChangePreview(asmdef.name, newFileName, GUI.skin.label);
        }
    }

    private void DrawChangePreview(string before, string after, GUIStyle style)
    {
        EditorGUILayout.BeginHorizontal(GUI.skin.box);
        
        EditorGUILayout.LabelField(before, style);
        EditorGUILayout.LabelField(after, style);
        
        EditorGUILayout.EndHorizontal();
    }

    private string FileField(string label, string path, string folderPanelTitle, string extension)
    {
        var rect = EditorGUILayout.GetControlRect();
        float padding = 5;
        float buttonWidth = 30;
            
        rect.width -= buttonWidth + padding;
            
        path = EditorGUI.TextField(rect, label, path);

        rect.x = rect.width + padding;
        rect.width = buttonWidth + 3;
            
        if (GUI.Button(rect, EditorGUIUtility.IconContent("FolderOpened On Icon")))
        {
            return EditorUtility.OpenFilePanel(folderPanelTitle, Application.dataPath, extension);
        }

        return path;
    }

    #endregion

    #region Helper Methods

    private string GetProjectPath()
    {
        return Application.dataPath.Remove(Application.dataPath.Length - "/Assets".Length);
    }

    private string GetPackageDirectoryPath()
    {
        return Path.GetDirectoryName(_manifestPath);
    }

    private string GetPackageFolderName()
    {
        return new DirectoryInfo(Path.GetDirectoryName(_manifestPath)).Name;
    }

    private bool IsSelectedPackage(string path)
    {
        string manifestFolderName = GetPackageFolderName();
        
        string[] folders = path.Split('/');
        string packageUrl = folders[1];

        return packageUrl == manifestFolderName;
    }

    private bool IsNameValid(string name)
    {
        return Regex.IsMatch(name, "^[a-z0-9._-]*$");
    }

    #endregion
}
