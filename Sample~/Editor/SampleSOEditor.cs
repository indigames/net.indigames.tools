using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ScriptableObjectBrowser;
using System;
using System.IO;
using UnityEditor;

public class SampleSOEditor : ScriptableObjectBrowserEditor<SampleSO>
{
    const string DEFAULT_NAME = "Sample";
    public SampleSOEditor()
    {
        //set this to true if you want to create a folder containing the scriptable object
        this.createDataFolder = false;
        //the storage path must be created before setting this
        this.defaultStoragePath = "Assets/GameUtils/ScriptableObjectBrowser/Sample/ScriptableObjects";
    }


    public override void ImportBatchData(string directory, Action<ScriptableObject> callback)
    {
        string[] allLines = File.ReadAllLines(directory);
        bool isSkippedFirstLine = false;
        foreach (string line in allLines)
        {
            if (!isSkippedFirstLine)
            {
                isSkippedFirstLine = true;
                continue;   
            }

            // get data form tsv file
            string[] splitedData = line.Split('\t');
            var id = splitedData[0];
            var name = DEFAULT_NAME + id;
            var path = this.defaultStoragePath + "/" + name + ".asset";


            SampleSO instance = null;
            // find instance if null create new
            instance = (SampleSO)AssetDatabase.LoadAssetAtPath(path, typeof(SampleSO));
            if (instance == null || !AssetDatabase.Contains(instance))
            {
                instance = ScriptableObject.CreateInstance<SampleSO>();
            }

            // import Data
            instance.ID = id;
            instance.name = splitedData[1];

            // Save data
            if (instance == null || !AssetDatabase.Contains(instance))
            {
                AssetDatabase.CreateAsset(instance, path);
                AssetDatabase.SaveAssets();
                callback(instance);
            }
            else
            {
                EditorUtility.SetDirty(instance);
            }
        }
    }
}
