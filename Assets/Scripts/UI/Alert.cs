using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Alert {
    GameObject obj, genericButton, okButton, cancelButton, messageObj, titleObj;

    System.Action genericAction, okAction, cancelAction;

    bool closed = false;

    void SetupCommon(GameObject parent, string title, string message, float height, bool leftAlign, float width = 400.0f) {
        obj = (GameObject)Object.Instantiate(Resources.Load("UIPrefabs/Alert"), new Vector3(0, 0, 0), Quaternion.identity);
        obj.transform.SetParent(parent.transform, true);
        var rt = obj.GetComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(0, 0);
        rt.sizeDelta = new Vector2(width, height);

        genericButton = obj.transform.Find("Button").gameObject;
        okButton = obj.transform.Find("OK").gameObject;
        cancelButton = obj.transform.Find("Cancel").gameObject;
        messageObj = obj.transform.Find("Message").gameObject;
        titleObj = obj.transform.Find("Title").gameObject;
        okButton.SetActive(false);
        cancelButton.SetActive(false);
        genericButton.SetActive(false);
        messageObj.GetComponent<Text>().text = message;
        titleObj.GetComponent<Text>().text = title;

        if (leftAlign) {
            messageObj.GetComponent<Text>().alignment = TextAnchor.UpperLeft;
        }

        genericButton.GetComponent<Button>().onClick.AddListener(delegate {
            ButtonPress();
        });
        okButton.GetComponent<Button>().onClick.AddListener(delegate {
            OK();
        });
        cancelButton.GetComponent<Button>().onClick.AddListener(delegate {
            Cancel();
        });
    }

    public Alert(GameObject parent, string title, string message, string buttonText,
        System.Action genericAction = null, float height = 180.0f, bool leftAlign = false, float width = 400.0f) {

        if (genericAction == null) genericAction = Close;
        SetupCommon(parent, title, message, height, leftAlign, width);
        this.genericAction = genericAction;
        genericButton.SetActive(true);
        genericButton.GetComponentInChildren<Text>().text = buttonText;
    }

    public Alert(GameObject parent, string title, string message, string okText, string cancelText,
        System.Action okAction = null, System.Action cancelAction = null, float height = 180.0f, bool leftAlign = false, float width = 400.0f) {

        if (okAction == null) okAction = Close;
        if (cancelAction == null) cancelAction = Close;
        SetupCommon(parent, title, message, height, leftAlign, width);
        this.okAction = okAction;
        this.cancelAction = cancelAction;
        okButton.SetActive(true);
        cancelButton.SetActive(true);
        okButton.GetComponentInChildren<Text>().text = okText;
        cancelButton.GetComponentInChildren<Text>().text = cancelText;
    }

    void Close() {
        closed = true;
        Object.Destroy(obj);
    }

    void ButtonPress() {
        genericAction.Invoke();
        Close();
    }

    void OK() {
        okAction.Invoke();
        Close();
    }

    void Cancel() {
        cancelAction.Invoke();
        Close();
    }

    public bool IsClosed() {
        return closed;
    }
}
