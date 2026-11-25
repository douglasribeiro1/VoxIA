namespace VoxIA
{
    /// <summary>
    /// Número do build. O GitHub Actions troca o 0 pelo número da compilação (ver build-windows.yml);
    /// a busca de atualizações compara com o version-windows.json publicado. 0 = compilado à mão,
    /// e aí não há busca automática de atualizações.
    /// </summary>
    public static class BuildInfo
    {
        public const int Build = 0;
    }
}
