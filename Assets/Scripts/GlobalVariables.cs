public static class GlobalVariables {
    //Increase the programVersion if and only if there are breaking changes
    //Don't increase it if there are only new features, and nothing else changed
    public static int programVersion = 1;

    //Increase the featureVersion if there are new features and no breaking changes
    //If the programVersion gets increased, reset featureVersion to 1
    //If missing in the settings of a core, it's considered as 0
    public static int featureVersion = 3;
}
