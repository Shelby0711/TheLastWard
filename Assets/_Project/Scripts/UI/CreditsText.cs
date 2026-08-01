namespace LastWard.UI
{
    /// <summary>
    /// The credits, as shown in the menu.
    ///
    /// Grouped by <b>what the player saw</b>, then by who made it — not by our filenames. A credits
    /// screen listing "Rig_Pugalo" or "base mesh" credits nobody: those are internal identifiers, and
    /// the person being thanked would not recognise their own work under them.
    ///
    /// Kept in code beside the screen that shows it rather than read from CREDITS.md at runtime: the
    /// markdown file is the legal record and carries licence terms and import dates nobody wants
    /// scrolling past a title screen. This is the human half. <b>Both must be updated together</b> —
    /// CREDITS.md is the one that matters if they ever disagree.
    ///
    /// Every CC-BY entry here is a shipping requirement, not a courtesy.
    /// </summary>
    public static class CreditsText
    {
        public const string Body =
"<color=#B82118>CHARACTER MODELS</color>\n" +
"   andriichykrii            David Glynch            zelmaht\n" +
"   JashiPSX                 xylvnking               Doomcubus\n" +
"   ShaxerTakkuY\n" +
"   Character rigging and animation by Mixamo (Adobe)\n" +
"\n" +
"<color=#B82118>ENVIRONMENT AND PROPS</color>\n" +
"   Creepy Stork Games       Claus the Modeler       trashbinkr\n" +
"   Brandon Westlake         chauhan bhautik         Em Marshall\n" +
"   Abimael Gonzalez         3dsauce                 yanix\n" +
"   raydev                   Gamecoder3D             lonesomeducky\n" +
"   Skkerat                  hoschu                  Homie\n" +
"   l0r3l3i                  Alpha                   Mateusz Wolinski\n" +
"   3Dexter                  Daniel Jurys\n" +
"\n" +
"<color=#B82118>TEXTURES</color>\n" +
"   Bradley D. (Torment Textures)\n" +
"   Screaming Brain Studios\n" +
"   MCSTEEG\n" +
"\n" +
"<color=#B82118>ART</color>\n" +
"   Balint Varga\n" +
"\n" +
"<color=#B82118>MUSIC</color>\n" +
"   \"Demented Nightmare\" by Darren Curtis\n" +
"\n" +
"<color=#B82118>SOUND EFFECTS</color>\n" +
"   Pixabay\n" +
"\n" +
"<color=#B82118>ADDITIONAL ASSETS</color>\n" +
"   Licensed via Fab.com under the Fab Standard License\n" +
"\n" +
"<color=#666666>Full licence terms and import dates are recorded in CREDITS.md.</color>";
    }
}
