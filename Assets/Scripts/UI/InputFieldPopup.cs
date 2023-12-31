using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class InputFieldPopup {
    GameObject obj, genericButton, okButton, cancelButton, inputFieldObj, titleObj, placeholderObj;

    System.Action<string> genericAction, okAction;
    System.Action cancelAction;

    bool closed = false;

    void SetupCommon(GameObject parent, string title, string placeholder, string initialText) {
        obj = (GameObject)Object.Instantiate(Resources.Load("UIPrefabs/InputFieldPopup"), new Vector3(0, 0, 0), Quaternion.identity);
        obj.transform.SetParent(parent.transform, true);
        var rt = obj.GetComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(0, 0);

        genericButton = obj.transform.Find("Button").gameObject;
        okButton = obj.transform.Find("OK").gameObject;
        cancelButton = obj.transform.Find("Cancel").gameObject;
        inputFieldObj = obj.transform.Find("InputField").gameObject;
        placeholderObj = inputFieldObj.transform.Find("Placeholder").gameObject;
        titleObj = obj.transform.Find("Title").gameObject;
        okButton.SetActive(false);
        cancelButton.SetActive(false);
        genericButton.SetActive(false);
        placeholderObj.GetComponent<Text>().text = placeholder;
        titleObj.GetComponent<Text>().text = title;
        if (initialText != null) inputFieldObj.GetComponent<InputField>().text = initialText;

        genericButton.GetComponent<Button>().onClick.AddListener(delegate {
            ButtonPress(inputFieldObj.GetComponent<InputField>().text);
        });
        okButton.GetComponent<Button>().onClick.AddListener(delegate {
            OK(inputFieldObj.GetComponent<InputField>().text);
        });
        cancelButton.GetComponent<Button>().onClick.AddListener(delegate {
            Cancel();
        });
    }

    public InputFieldPopup(GameObject parent, string title, string placeholder, string buttonText, System.Action<string> genericAction = null, string initialText = null) {
        if (genericAction == null) genericAction = CloseStr;
        SetupCommon(parent, title, placeholder, initialText);
        this.genericAction = genericAction;
        genericButton.SetActive(true);
        genericButton.GetComponentInChildren<Text>().text = buttonText;
    }

    public InputFieldPopup(GameObject parent, string title, string placeholder, string okText, string cancelText, System.Action<string> okAction = null, System.Action cancelAction = null, string initialText = null) {
        if (okAction == null) okAction = CloseStr;
        if (cancelAction == null) cancelAction = Close;
        SetupCommon(parent, title, placeholder, initialText);
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

    void CloseStr(string str) {
        Close();
    }

    void ButtonPress(string str) {
        genericAction.Invoke(str);
        Close();
    }

    void OK(string str) {
        okAction.Invoke(str);
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
