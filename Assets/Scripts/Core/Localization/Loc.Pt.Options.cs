// Loc.Pt.Options.cs
// Portuguese for the options menu and Unity quality-level names.

using System.Collections.Generic;

namespace TheWaningBorder.Core.Localization
{
    public static partial class Loc
    {
        private static void AddOptions(Dictionary<string, string> t)
        {
            // ── Gameplay settings (2026-09-08) ──────────────────────────
            t["GAMEPLAY"] = "JOGABILIDADE";
            t["SHOW HEALTH BARS"] = "MOSTRAR BARRAS DE VIDA";
            t["Whose health you see without pointing at them."] =
                "De quem vê a vida sem apontar para eles.";
            t["DRAG SELECTION"] = "SELEÇÃO POR ARRASTO";
            t["What a box-select keeps when it catches both kinds. Hold Ctrl or Alt to take everything."] =
                "O que a seleção por caixa mantém quando apanha os dois tipos. Ctrl ou Alt leva tudo.";
            // Health-bar modes
            t["Always"] = "Sempre";
            t["Own"] = "Suas";
            t["Friendly"] = "Aliadas";
            t["Smart"] = "Inteligente";
            t["None"] = "Nenhuma";
            // Drag priority
            t["Economy"] = "Economia";
            t["Military"] = "Militar";
            t["Off"] = "Desligado";

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

            // ── SettingsMenu.unity authored labels (scene localizer) ────────
            // Uppercase like every other screen built in the Skirmish look;
            // the captions are the 24pt hints under each option label.
            t["PROFILE"] = "PERFIL";
            t["DISPLAY"] = "ECRÃ";
            t["AUDIO"] = "ÁUDIO";
            t["PLAYER NAME"] = "NOME DO JOGADOR";
            t["GRAPHICS QUALITY"] = "QUALIDADE GRÁFICA";
            t["RESOLUTION"] = "RESOLUÇÃO";
            t["FULLSCREEN"] = "ECRÃ INTEIRO";
            t["MASTER VOLUME"] = "VOLUME GERAL";
            t["MUSIC VOLUME"] = "VOLUME DA MÚSICA";
            t["LANGUAGE"] = "IDIOMA";
            t["APPLY"] = "APLICAR";
            t["Shown to other players in a lobby."] = "Mostrado aos outros jogadores numa sala.";
            t["Higher looks better and costs frames."] = "Mais alta fica melhor e custa fotogramas.";
            t["Pick the mode that fills your monitor."] = "Escolhe o modo que preenche o teu monitor.";
            t["Borderless full screen, or a window."] = "Ecrã inteiro sem margens, ou uma janela.";
            t["Everything the game plays."] = "Tudo o que o jogo reproduz.";
            t["The score only."] = "Apenas a banda sonora.";
            t["Shown in its own language, so you can always find the way back."] =
                "Mostrado no seu próprio idioma, para que consigas sempre voltar atrás.";
            t["Takes effect when you press APPLY."] = "Entra em vigor quando premires APLICAR.";

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
