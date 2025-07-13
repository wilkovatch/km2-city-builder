using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using States;
using System;
using RuntimeCalculator;

public class CityProperties : ParametricObjectContainer<CityElements.Types.CitySettings.CitySettings>, IObjectWithState {
    public ObjectState state;
    Dictionary<string, string> paramMap = null;
    public Dictionary<string, (string type, CityElements.Types.CitySettings.ParameterInjection injection)> injectableParamMap = null;
    CityElements.Types.CitySettings.CitySettings settings = null;
    ObjectState oldState = null;
    CityElements.Types.Runtime.CitySettings runtimeSettings;
    VariableContainer vc;

    public override ObjectState[] GetContainerStateArrayForParametricObjects() {
        return new ObjectState[] { state };
    }

    public override ObjectState GetContainerStateForParametricObjects() {
        return state;
    }

    public override VariableContainer GetParametricObjectVariableContainer() {
        return vc;
    }

    public override CityElements.Types.Runtime.RuntimeType<CityElements.Types.CitySettings.CitySettings> GetRuntimeType() {
        return runtimeSettings;
    }

    public ObjectState GetState() {
        return state;
    }

    public void SetState(ObjectState newState) {
        state = newState;
    }

    internal virtual void Initialize() {
        state = new ObjectState();
        state.Name = "cityProperties";
        vc = new VariableContainer(new List<(string, Type)>());
    }

    public void SyncSettings() {
        repositionObjects = true;
        var allSettings = CityElements.Types.Parsers.TypeParser.GetCitySettings();
        if (allSettings != null && allSettings.ContainsKey("city")) {
            var newSettings = allSettings["city"].typeData;
            if (newSettings != settings) {
                settings = newSettings;
                runtimeSettings = new CityElements.Types.Runtime.CitySettings(settings);
                paramMap = new Dictionary<string, string>();
                foreach (var param in settings.parametersInfo.parameters) {
                    paramMap.Add(param.name, param.type);
                }
                injectableParamMap = new Dictionary<string, (string type, CityElements.Types.CitySettings.ParameterInjection injection)>();
                foreach (var param in settings.settings.injectableParameters) {
                    injectableParamMap.Add(param.parameter, (paramMap[param.parameter], param));
                }
            }
        }
    }

    public bool ParameterHasChanged(string parameter) {
        if (oldState == null) return true;
        var entry = injectableParamMap[parameter];
        var type = entry.type;
        switch (type) {
            case "string":
            case "mesh":
                return state.Str(parameter) != oldState.Str(parameter);
            case "bool":
                return state.Bool(parameter) != oldState.Bool(parameter);
            case "float":
                return state.Float(parameter) != oldState.Float(parameter);
            case "int":
                return state.Int(parameter) != oldState.Int(parameter);
            default:
                print("Unsupported injection type: " + type);
                return false;
        }
    }

    public void UpdateOlds() {
        oldState = (ObjectState)(state.Clone());
    }

    public void InjectParameter(string parameter, ObjectState outState) {
        var entry = injectableParamMap[parameter];
        var type = entry.type;
        var newName = "_GLOBAL_" + parameter;
        switch (type) {
            case "string":
            case "mesh":
                outState.SetStr(newName, state.Str(parameter));
                break;
            case "bool":
                outState.SetBool(newName, state.Bool(parameter));
                break;
            case "float":
                outState.SetFloat(newName, state.Float(parameter));
                break;
            case "int":
                outState.SetInt(newName, state.Int(parameter));
                break;
            default:
                print("Unsupported injection type: " + type);
                break;
        }
    }
}
