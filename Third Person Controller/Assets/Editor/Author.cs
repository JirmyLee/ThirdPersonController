//===================================================
// FileName:      #SCRIPTNAME#.cs         
// Created:       #AuthorName#
// CreateTime:    #CreateTime#
// E-mail:        #E-mail#
// Description:   
//===================================================
using System.IO;
using System;
using UnityEditor;

[Obsolete]
public class Copyright : AssetModificationProcessor
{
    private const string AuthorName = "Allent Lee";
    private const string DateFormat = "yyyy-MM-dd HH:mm:ss";
    private const string AuthorEmail = "xiaomo_lzm@163.com";
    private static void OnWillCreateAsset(string path)
    {
        path = path.Replace(".meta", "");
        if (path.EndsWith(".cs"))
        {
            string allText = File.ReadAllText(path);
            allText = allText.Replace("#AuthorName#", AuthorName);
            allText = allText.Replace("#E-mail#", AuthorEmail);
            allText = allText.Replace("#CreateTime#", System.DateTime.Now.ToString(DateFormat));
            File.WriteAllText(path, allText);
            AssetDatabase.Refresh();
        }
    }
}


