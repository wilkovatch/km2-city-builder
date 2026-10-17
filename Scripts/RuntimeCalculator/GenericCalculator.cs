namespace RuntimeCalculator {
    public interface GenericCalculator<T> {
        public T GetValue(VariableContainer vc);
        public void SetIndices(VariableContainer vc);
    }
}
