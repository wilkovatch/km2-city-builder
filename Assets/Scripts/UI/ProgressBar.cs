using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ProgressBar {
    GameObject obj;
    Image progressBar;
    Text progressText, messageText;

    public ProgressBar(GameObject parent, string message) {
        obj = (GameObject)Object.Instantiate(Resources.Load("UIPrefabs/LoadingPanel"), new Vector3(0, 0, 0), Quaternion.identity);
        obj.transform.SetParent(parent.transform, true);
        var rt = obj.GetComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(0, 0);

        var bgBar = obj.transform.Find("BackgroundBar");
        progressBar = bgBar.Find("GreenBar").gameObject.GetComponent<Image>();
        progressText = bgBar.Find("Text").gameObject.GetComponent<Text>();
        progressBar.fillAmount = 0;
        progressText.text = "0%";

        messageText = obj.transform.Find("Message").GetComponent<Text>();
        messageText.text = message;
    }

    public void SetText(string message) {
        messageText.text = message;
    }

    public void SetProgress(float percent) {
        progressBar.fillAmount = percent;
        progressText.text = Mathf.RoundToInt(percent * 100) + "%";
    }

    public void SetActive(bool active) {
        obj.SetActive(active);
    }
}
