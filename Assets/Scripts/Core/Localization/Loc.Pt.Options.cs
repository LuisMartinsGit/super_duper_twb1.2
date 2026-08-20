// Loc.Pt.Options.cs
// Portuguese for the options menu and Unity quality-level names.
// Location: Assets/Scripts/Core/Localization/Loc.Pt.Options.cs

using System.Collections.Generic;

namespace TheWaningBorder.Core.Localization
{
    public static partial class Loc
    {
        private static void AddOptions(Dictionary<string, string> t)
        {
            t["OPTIONS"] = "OPÇÕES";
            t["Graphics Quality"] = "Qualidade Gráfica";
            t["Resolution"] = "Resolução";
            t["Display Mode"] = "Modo de Ecrã";
            t["Windowed"] = "Janela";
            t["Fullscreen"] = "Ecrã Inteiro";
            t["Master Volume"] = "Volume Geral";
            t["Music Volume"] = "Volume da Música";
            t["Language"] = "Idioma";
            t["Back"] = "Voltar";
            t["Apply"] = "Aplicar";
            t["Settings applied!"] = "Definições aplicadas!";
            t["Unknown"] = "Desconhecido";

            // Unity quality-level names (QualitySettings.names) — the
            // project's levels plus Unity's default ladder so a template
            // change stays covered.
            t["Very Low"] = "Muito Baixa";
            t["Low"] = "Baixa";
            t["Medium"] = "Média";
            t["High"] = "Alta";
            t["Very High"] = "Muito Alta";
            t["Ultra"] = "Ultra";
            t["Performant"] = "Desempenho";
            t["Balanced"] = "Equilibrada";
            t["High Fidelity"] = "Alta Fidelidade";
        }
    }
}
