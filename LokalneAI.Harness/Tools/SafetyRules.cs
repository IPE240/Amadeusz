using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace LokalneAI.Harness.Tools
{
    /// <summary>
    /// Zasady strażnika bezpieczeństwa (<see cref="SafetyGuard"/>): co model może robić z komputerem, biurem, siecią i samym Amadeuszem.
    /// Opis dla ludzi i uzasadnienia: <c>docs\bezpieczenstwo.md</c>. Zmiana listy = zmiana tam i w testach <c>SafetyTests</c>.
    /// </summary>
    internal static class SafetyRules
    {
        internal const string Self = "samo_amadeusz";
        internal const string Exposure = "siec_dostep";
        internal const string Secrets = "sekrety";
        internal const string Autostart = "autostart";
        internal const string Defenses = "zabezpieczenia";
        internal const string Privileges = "uprawnienia";
        internal const string Leak = "wyciek";
        internal const string RemoteCode = "zdalne_wykonanie";
        internal const string Office = "siec_biura";
        internal const string Destroy = "niszczenie";
        internal const string Risky = "ryzykowne";

        internal const int MaxSearchPhrase = 200;

        /// <summary>Dłuższego polecenia od modelu strażnik nie ocenia — model pisze krótkie polecenia, a długi tekst mógłby ukryć zamiar.</summary>
        internal const int MaxScriptChars = 16000;

        /// <summary>Największy plik skryptu (.ps1, .bat, .py), którego treść strażnik sprawdza przed uruchomieniem; większy — blokada.</summary>
        internal const int MaxScriptFileBytes = 256 * 1024;

        private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        /// <summary>Limit czasu jednego wzorca (ochrona przed tekstem, który zawiesiłby sprawdzanie).</summary>
        private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

        /// <summary>Zasada na tekście skryptu (po <see cref="Normalize"/>).</summary>
        private sealed class Rule
        {
            public Rule(string category, string pattern, string reason, bool onlyWhenChanging = false, bool caseSensitive = false, bool powerShellOnly = false)
            {
                Category = category;
                Pattern = new Regex(pattern, caseSensitive ? RegexOptions.CultureInvariant : Options, MatchTimeout);
                Reason = reason;
                OnlyWhenChanging = onlyWhenChanging;
                CaseSensitive = caseSensitive;
                PowerShellOnly = powerShellOnly;
            }

            public string Category { get; }

            public Regex Pattern { get; }

            public string Reason { get; }

            /// <summary>Tylko gdy skrypt coś zmienia (samo czytanie, np. odczyt klucza Run, jest dozwolone).</summary>
            public bool OnlyWhenChanging { get; }

            /// <summary>Wielkość liter ma znaczenie (np. <c>curl -F</c> wysyła plik, a <c>-f</c> nie).</summary>
            public bool CaseSensitive { get; }

            /// <summary>Wzorzec składni PowerShella (np. <c>.Bind(</c>) — w Pythonie i JavaScripcie znaczy co innego.</summary>
            public bool PowerShellOnly { get; }
        }

        // -------------------------------------------------------------------------------------------------- blokady

        private static readonly Rule[] BlockRules =
        {
            // samo_amadeusz — uruchamianie, wołanie i zmiana samego siebie (ścieżki sprawdza osobno CheckSelfReferences)
            new Rule(Self, @"--(autotest|harness|udostepnij)\b", "uruchamianie Amadeusza z przełącznikami kontroli — Amadeusz nie uruchamia sam siebie"),
            new Rule(Self, @"\b(lokalneai(\.app)?|amadeusz(\.stary)?)\.exe\b|\blokalneai\.eval\b", "uruchamianie albo zmiana programu Amadeusz", onlyWhenChanging: true),
            new Rule(Self, @"\bollama(\.exe)?['""]?\s+(pull|push|rm|cp|create|serve|run|stop|signin|signout|launch|copy|delete)\b", "zarządzanie modelami albo serwerem Ollamy — modele pobiera i usuwa użytkownik (instalator, ollama w PowerShellu)"),
            new Rule(Self, @"\bollama_[a-z_]+\b", "zmienne OLLAMA_* zmieniają, kto i jak może łączyć się z modelem", onlyWhenChanging: true),
            new Rule(Self, @"\b(stop-process|spps|kill|taskkill(\.exe)?|stop-service|sc(\.exe)?\s+stop)\b[^\n]*\b(lokalneai|amadeusz|ollama)\b|\b(lokalneai|amadeusz|ollama)\b[^\n]*\|\s*(stop-process|spps|kill)\b", "zamykanie Amadeusza albo Ollamy"),
            new Rule(Self, @"/api/(chat|generate|embed|embeddings|pull|push|delete|create|copy|blobs)\b|/v1/(chat/completions|completions|embeddings|responses|messages)\b", "wołanie modelu przez jego API z pominięciem Amadeusza (bez zasad, zgód i strażnika)", onlyWhenChanging: true),

            // siec_dostep — otwieranie komputera na sieć
            new Rule(Exposure, @"\b(new|set|enable|disable|remove|copy|rename)-netfirewall\w*", "zmiana zapory Windows — port otwiera użytkownik (Ustawienia → Udostępnianie → Otwórz port w zaporze…)"),
            new Rule(Exposure, @"\bnetsh(\.exe)?\b(?![^\n]*\bshow\b)[^\n]*\b(advfirewall|firewall|portproxy|urlacl|sslcert|wlan\s+(set|add|delete|export|connect|start)|winhttp\s+(set|import|reset)|routing|bridge|ras|rpc|http\s+(add|delete)|interface\s+\S+\s+(set|add|delete))\b", "zmiana ustawień sieci przez netsh (zapora, przekierowanie portów, proxy)"),
            new Rule(Exposure, @"\b(new|set|grant|revoke|remove|block|unblock)-smb(share|shareaccess|serverconfiguration|clientconfiguration)\b|\bnet(1)?(\.exe)?\s+share\s+\S+\s*=|\bnet(1)?(\.exe)?\s+share\s+\S+\s+/(grant|delete|users|cache|remark)", "udostępnianie folderów w sieci"),
            new Rule(Exposure, @"\bset-netconnectionprofile\b|\bset-netipinterface\b[^\n]*-forwarding\b", "zmiana profilu albo trasowania sieci"),
            new Rule(Exposure, @"\benable-psremoting\b|\bset-wsmanquickconfig\b|\bwinrm(\.cmd|\.vbs)?\s+(qc|quickconfig|set|create|put|invoke|i)\b|\b(set|new)-item\s+wsman:|\bwsman:\\|\benable-wsmancredssp\b|\b(set|register)-pssessionconfiguration\b", "zdalne zarządzanie (WinRM, PowerShell Remoting)"),
            new Rule(Exposure, @"\bfdenytsconnections\b|\\terminal server\b|\brdp-tcp\b|\btermservice\b|\bset-rdpsettings\b", "włączanie pulpitu zdalnego", onlyWhenChanging: true),
            new Rule(Exposure, @"\bopenssh\.server\b|\bsshd(\.exe)?\b|\bauthorized_keys\b", "serwer SSH albo klucze dostępu SSH"),
            new Rule(Exposure, @"\b(ssh|plink)(\.exe)?\b[^\n]*\s-[a-z]*[rldw]\s*[\[\w.:]*\d|\bssh(\.exe)?\b[^\n]*-o\s*['""]?(remoteforward|localforward|dynamicforward|proxycommand|localcommand)", "tunel SSH (przekierowanie portów)"),
            new Rule(Exposure, @"\b(ngrok|cloudflared|frpc|frps|chisel|tailscale|tailscaled|zerotier\w*|socat|ncat|netcat|localtunnel|pinggy|serveo|devtunnel|wstunnel)\b|\bnc(64)?(\.exe)?\s+-[a-z]*[lenvp]|\bbore\s+local\b|\bcode(\.cmd)?\s+tunnel\b", "tunel albo przekaźnik wystawiający komputer do internetu"),
            new Rule(Exposure, @"\b(system\.)?net\.(sockets\.(tcplistener|udpclient|socket)|httplistener)\b|\btcplistener\b|\bhttplistener\b|\bpython[0-9.]*(\.exe)?\s+-m\s+(http\.server|simplehttpserver|smtpd|pyftpdlib|uploadserver)\b|\bhttp-server\b|\bnpx(\.cmd)?\s+(serve|http-server)\b|\bphp(\.exe)?\s+-s\b|\bstart-podeserver\b", "nasłuch na porcie (serwer na tym komputerze)"),
            new Rule(Exposure, @"\.(bind|listen|acceptsocket|accepttcpclient|begingetcontext|getcontext)\s*\(", "nasłuch na porcie (serwer na tym komputerze)", powerShellOnly: true),
            new Rule(Exposure, @"\b(anydesk|teamviewer|tv_w32|tv_x64|rustdesk|tightvnc|ultravnc|realvnc|winvnc|tvnserver|vncserver|screenconnect|connectwise|logmein|splashtop|remotepc|supremo|dwagent|meshagent|ammyy|radmin|rutserv|getscreen|remoting_host|quickassist|msra)\b", "programy zdalnego dostępu uruchamia i instaluje tylko użytkownik", onlyWhenChanging: true),
            new Rule(Exposure, @"\\drivers\\etc\\hosts\b|internet settings[^\n]*(proxyserver|proxyenable|autoconfigurl)", "zmiana pliku hosts albo serwera proxy", onlyWhenChanging: true),

            // sekrety — także samo czytanie
            new Rule(Secrets, @"\bklucze\.json\b|\.local\.json\b|\.git-credentials\b|[\\/]\.ssh([\\/]|\b)|~[\\/]\.ssh\b|\bid_(rsa|dsa|ecdsa|ed25519)\b|\.(pem|pfx|p12|ppk|kdbx|kdb|jks|keystore)\b|[\\/]\.gnupg\b|\.aws[\\/]credentials|[\\/]\.azure\b|\.docker[\\/]config\.json|[\\/]\.kube[\\/]config|\.npmrc\b|\.pypirc\b|[\\/_.]netrc\b", "pliki z hasłami albo kluczami"),
            new Rule(Secrets, @"(^|[\\/\s'""])\.env(\.[a-z]+)?(['""\s]|$)", "plik .env z hasłami albo kluczami"),
            new Rule(Secrets, @"\\login data\b|\\cookies\b|\\local state\b|\\web data\b|\blogins\.json\b|\bkey[34]\.db\b|\\microsoft\\(credentials|protect|vault)\b|\bntuser\.dat\b|\\config\\(sam|system|security)\b", "hasła przeglądarek, poświadczenia albo gałęzie rejestru z hasłami"),
            new Rule(Secrets, @"\bcmdkey(\.exe)?\b|\bvaultcmd(\.exe)?\b|\bget-storedcredential\b|\bcredentialmanager\b|\bpasswordvault\b|\bcred(read|enumerate|backup)\b|\bprotecteddata\b|\bunprotect\s*\(|\bcryptunprotectdata\b|\bdpapi\b|\bconvertfrom-securestring\b|\bsecurestringtobstr\b|\bptrtostring(auto|bstr)\b|getnetworkcredential\(\)|\bget-credential\b|\bpromptforcredential\b", "Menedżer poświadczeń, odszyfrowywanie albo prośba o hasło"),
            new Rule(Secrets, @"\bgit(\.exe)?\s+credential\b|\bcredential\.helper\b|\bgit-credential-", "poświadczenia gita (tokeny repozytoriów)"),
            new Rule(Secrets, @"\bkey\s*=\s*clear\b|\bwlan\s+export\s+profile\b", "hasła sieci Wi-Fi"),
            new Rule(Secrets, @"\bmimikatz|\bsekurlsa\b|\blsadump\b|\bkerberos::|\blazagne\b|\bprocdump(64)?(\.exe)?\b|\bminidump\b|\bcomsvcs(\.dll)?\b|\blsass\b|\breg(\.exe)?\s+save\s+hk(lm|ey_local_machine)\\(sam|system|security)\b|\bntdsutil\b|\bntds\.dit\b|\bexport-pfxcertificate\b|\bsecretsdump\b|\bpwdump\b", "wydobywanie haseł z pamięci albo rejestru"),

            // autostart — programy uruchamiane same po starcie
            new Rule(Autostart, @"\b(register|new|set|enable|disable|start|unregister)-scheduledtask(action|trigger|settingsset|principal)?\b|\bschtasks(\.exe)?\b[^\n]*/(create|change|run|delete)\b|\bat(\.exe)?\s+\d{1,2}:\d{2}\b|\bschedule\.service\b", "harmonogram zadań Windows"),
            new Rule(Autostart, @"\bnew-service\b|\bremove-service\b|\bsc(\.exe)?\s+(create|config|failure|sdset|delete)\b|\bset-service\b[^\n]*-(startuptype|binarypathname|credential)\b|\binstallutil\b", "tworzenie albo zmiana usług Windows"),
            new Rule(Autostart, @"\\currentversion\\(run|runonce|runservices|runservicesonce|policies\\explorer\\run|explorer\\(user )?shell folders|winlogon|windows\\(load|run)|image file execution options|silentprocessexit)\b|\\winlogon\b|\buserinit\b|\bappinit_dlls\b|\\image file execution options\\|\\environment\\[^\n]*userinitmprlogonscript|\\software\\classes\\[^\n]*\\shell\\[^\n]*\\command|\binprocserver32\b|\\active setup\\installed components|\bbootexecute\b|\\session manager\\|\bnetsh\b[^\n]*\badd\s+helper\b", "wpis autostartu albo przejęcia w rejestrze", onlyWhenChanging: true),
            new Rule(Autostart, @"\\start menu\\programs\\startup\b|\bshell:(common )?startup\b|getfolderpath\(\s*['""]?(common)?startup|specialfolder\]::(common)?startup", "folder Autostart", onlyWhenChanging: true),
            new Rule(Autostart, @"__eventfilter|__eventconsumer|commandlineeventconsumer|activescripteventconsumer|__filtertoconsumerbinding|\bregister-wmievent\b|\bregister-cimindicationevent\b", "subskrypcje zdarzeń WMI (ukryty autostart)"),
            new Rule(Autostart, @"\b(add-content|set-content|out-file|new-item|copy-item|move-item|tee-object|ac|sc|ni)\b[^\n]*\$profile\b|>>?\s*\$profile\b|\$profile\.(currentuser|allusers)\w*|microsoft\.powershell(ise)?_profile\.ps1|\\(windows)?powershell\\profile\.ps1|\\(windows)?powershell\\modules\\", "profil albo moduły PowerShella (kod przy każdym starcie PowerShella)", onlyWhenChanging: true),
            new Rule(Autostart, @"\\start menu\\|\breg(\.exe)?\s+(import|restore|load)\b|\bregedit(\.exe)?\b[^\n]*\s/s\b", "Menu Start albo hurtowy import rejestru", onlyWhenChanging: true),
            new Rule(Autostart, @"\bsetx(\.exe)?\s+(/m\s+)?path\b|setenvironmentvariable\(\s*['""]path['""]|\\session manager\\environment|hkcu:\\environment[^\n]*\bpath\b", "trwała zmiana zmiennej PATH"),
            new Rule(Autostart, @"\\microsoft\\(word|excel)\\startup\\|\\xlstart\\|\bnormal\.dotm\b|\bpersonal\.xlsb\b", "dodatki uruchamiane przy starcie Worda albo Excela", onlyWhenChanging: true),

            // zabezpieczenia — wyłączanie ochrony
            new Rule(Defenses, @"\b(set|add|remove)-mppreference\b|\bdisablerealtimemonitoring\b|\bdisableioavprotection\b|\bdisablebehaviormonitoring\b|\bexclusion(path|process|extension|ipaddress)\b|\bmpcmdrun(\.exe)?\b[^\n]*-(removedefinitions|disableservice)|\btamperprotection\b|\bdisableantispyware\b|\bdisableantivirus\b", "wyłączanie albo osłabianie Microsoft Defender"),
            new Rule(Defenses, @"\\windows defender\\", "ustawienia Microsoft Defender w rejestrze", onlyWhenChanging: true),
            new Rule(Defenses, @"\b(stop|set|suspend|disable|remove)-service\b[^\n]*\b(windefend|mpssvc|wscsvc|securityhealthservice|sense|wdnissvc|eventlog|bfe|mpsdrv|sysmon\w*|wdboot|wdfilter)\b|\b(sc|net)(\.exe)?\s+(stop|config|delete|pause)\s+['""]?(windefend|mpssvc|wscsvc|securityhealthservice|sense|wdnissvc|eventlog|bfe|sysmon\w*)\b", "zatrzymywanie usług bezpieczeństwa"),
            new Rule(Defenses, @"\benablelua\b|\bconsentpromptbehavior(admin|user)\b|\bpromptonsecuredesktop\b|\blocalaccounttokenfilterpolicy\b|\bfilteradministratortoken\b", "kontrola konta użytkownika (UAC)", onlyWhenChanging: true),
            new Rule(Defenses, @"\bwevtutil(\.exe)?\s+(cl|clear-log|sl|set-log|um)\b|\bclear-eventlog\b|\bremove-eventlog\b|\blimit-eventlog\b|\bclear-winevent\b|\bauditpol(\.exe)?\b[^\n]*/(set|clear|remove|restore)|\bset-processmitigation\b|\bfsutil(\.exe)?\s+usn\s+deletejournal\b", "czyszczenie albo wyłączanie dzienników zdarzeń"),
            new Rule(Defenses, @"\bamsi(utils|initfailed|scanbuffer|context|session)\b|\bamsi\.dll\b|\betweventwrite\b|\bscriptblocklogging\b|\bmodulelogging\b|\bpsetwlogprovider\b", "obchodzenie ochrony skryptów (AMSI, rejestrowanie PowerShella)"),
            new Rule(Defenses, @"\bset-executionpolicy\b|\\executionpolicy\b", "zasady uruchamiania skryptów", onlyWhenChanging: true),
            new Rule(Defenses, @"\bunblock-file\b|\bzone\.identifier\b|\bsmartscreen(enabled)?\b", "zdejmowanie oznaczenia pliku z internetu (SmartScreen)", onlyWhenChanging: true),

            // uprawnienia — konto, administrator, prawa do plików
            new Rule(Privileges, @"-verb\s+['""]?runas\b|\brunas(\.exe)?\b|\bgsudo\b|\bsudo(\.exe)?\s|\bnsudo|verb\s*=\s*['""]runas|\bshellexecute\b[^\n]*runas", "uruchamianie jako administrator"),
            new Rule(Privileges, @"\bnet(1)?(\.exe)?\s+(user|localgroup|group|accounts)\b[^\n]*(/add|/delete|/del\b|/active|/domain|/passwordreq|/expires)|\bnet(1)?(\.exe)?\s+user\s+\S+\s+\S+|\b(new|set|remove|enable|disable|rename)-localuser\b|\b(add|remove)-localgroupmember\b|\bnew-localgroup\b|\b(new|set)-aduser\b|\badd-adgroupmember\b|\bset-adaccountpassword\b|\bds(add|mod)\b|\bwmic\s+useraccount\b", "konta i grupy użytkowników"),
            new Rule(Privileges, @"\btakeown(\.exe)?\b|\bicacls(\.exe)?\b[^\n]*/(grant|setowner|reset|deny|remove|setintegritylevel|restore)\b|\bset-acl\b|\bx?cacls(\.exe)?\b|\bsubinacl\b|\bsetacl\b", "przejmowanie plików albo zmiana uprawnień do nich"),

            // wyciek — wysyłanie danych z komputera
            new Rule(Leak, @"\b(invoke-webrequest|iwr|invoke-restmethod|irm|curl|wget)\b[^\n]*\s-(method\s+['""]?(post|put|patch|delete|merge)|body|infile|form)\b", "wysyłanie danych przez sieć (POST, -Body, -InFile)"),
            new Rule(Leak, @"(?i:\bcurl(\.exe)?\b)[^\n]*\s(-d\b|(?i:--data)|-F\b|(?i:--form)|-T\b|(?i:--upload-file|--json\b)|-X\s*['""]?(?i:post|put|patch)\b|(?i:--request)\s+['""]?(?i:post|put|patch)\b)", "wysyłanie danych przez curl", caseSensitive: true),
            new Rule(Leak, @"\.(uploadfile|uploadstring|uploaddata|uploadvalues)(async)?\s*\(|\.(postasync|putasync|patchasync|sendasync|getrequeststream)\s*\(|\bstart-bitstransfer\b[^\n]*-transfertype\s+upload", "wysyłanie plików albo danych przez sieć"),
            new Rule(Leak, @"\bnet\.(webclient|webrequest|httpwebrequest|ftpwebrequest)\b|\b(system\.net\.http\.)?httpclient\b|\bxmlhttp\b|\bwinhttp\b", "klasy .NET do połączeń sieciowych — do pobierania służy Invoke-WebRequest z adresem wprost"),
            new Rule(Leak, @"\bsend-mailmessage\b|\bnet\.mail\b|\bsmtpclient\b|\bmailkit\b|\bcdo\.message\b|\boutlook\.application\b|\bmapi\.session\b|\bblat(\.exe)?\b|\bsendemail(\.exe)?\b", "wysyłanie poczty (szkic maila robi umiejętność office — wysyła użytkownik)"),
            new Rule(Leak, @"\baws(\.exe)?\s+s3\b|\bmega(cmd|-put)\b|\bnet\.ftpwebrequest\b", "przesyłanie plików poza komputer"),
            new Rule(Leak, @"\[(system\.)?net\.dns\]::\w+\(\s*[^'"")\s]", "zapytanie DNS o nazwę budowaną ze zmiennych"),

            // zdalne_wykonanie — kod uruchamiany w ukryciu albo spoza skryptu
            new Rule(RemoteCode, @"\b(invoke-expression|iex)\b", "wykonywanie tekstu jako kodu (Invoke-Expression) — napisz polecenie wprost"),
            new Rule(RemoteCode, @"\bscriptblock\]::create\b|\bnewscriptblock\b|\.invokecommand\.|\$executioncontext\b|\.invoke(returnasis)?\s*\(|\binvokescript\b|\baddscript\s*\(|\bpowershell\]::create\b|\brunspacefactory\b|\.getnewclosure\s*\(", "wykonywanie kodu budowanego w locie — napisz polecenie wprost"),
            new Rule(RemoteCode, @"\breflection\.assembly\]::|\bassembly\]::(load|loadfile|loadfrom|unsafeloadfrom)\b|\bappdomain\]::currentdomain\.(load|definedynamicassembly)|\bdefinedynamicassembly\b|\bactivator\]::createinstance\b|\bgettypefrom(progid|clsid)\b|\btype\]::gettype\(|\.getmethod\(|\.getfield\(|\bnonpublic\b|\bmarshal\]::|\bvirtualalloc\b|\bcreatethread\b|\bwriteprocessmemory\b|\bdllimport\b", "ładowanie kodu .NET albo bibliotek systemowych w locie"),
            new Rule(RemoteCode, @"(^|\s)-e(c|n|nc|nco|ncod|ncode|ncoded|ncodedc|ncodedco|ncodedcom|ncodedcomm|ncodedcomma|ncodedcomman|ncodedcommand)?\s+[a-z0-9+/=]{16,}|\bfrombase64(string)?\b|\bcertutil(\.exe)?\b[^\n]*-decode\b|\bb64decode\b", "polecenie zakodowane (base64) — napisz polecenie wprost"),
            new Rule(RemoteCode, @"\[char\[\]\]\s*\(?\s*(0x)?\d|-join\s*\(?\s*\[char|\[string\]::join\([^\n]*\[char|-bxor\b", "tekst składany z kodów znaków — napisz polecenie wprost"),
            new Rule(RemoteCode, @"\b(new|set)-alias\b|\b(set|new)-item\s+['""]?(alias|function):|\$function:", "aliasy i funkcje podmieniające polecenia — napisz polecenie wprost"),
            new Rule(RemoteCode, @"\$\{?env:comspec\b|%comspec%", "uruchamianie interpretera poleceń przez zmienną COMSPEC"),
            new Rule(RemoteCode, @"\b(invoke-command|icm)\b", "Invoke-Command — napisz polecenia wprost"),
            new Rule(RemoteCode, @"\b(start-job|start-threadjob|register-objectevent|register-engineevent)\b|\bset-psbreakpoint\b[^\n]*-action", "kod uruchamiany w tle albo na zdarzenie"),
            new Rule(RemoteCode, @"\bwin32_process\b[^\n]*\bcreate\b|\bwmiclass\b|\binvoke-wmimethod\b|\binvoke-cimmethod\b|\bwin32_scheduledjob\b|\bwmic(\.exe)?\b", "uruchamianie programów przez WMI"),
            new Rule(RemoteCode, @"-comobject\s+['""]?(wscript\.shell|wscript\.network|shell\.application|mmc20\.application|shellwindows|shellbrowserwindow|schedule\.service|hnetcfg\.\w+|outlook\.application|word\.application|excel\.application|powerpoint\.application|access\.application|internetexplorer\.application|msxml2\.(server)?xmlhttp|microsoft\.xmlhttp|winhttp\.winhttprequest|adodb\.stream|msscriptcontrol\.scriptcontrol|scriptcontrol)\b", "obiekt COM, który uruchamia programy, wysyła dane albo steruje Office’em poza umiejętnością office"),
            new Rule(RemoteCode, @"\b(mshta|rundll32|regsvr32|cscript|wscript|msbuild|regasm|regsvcs|cmstp|odbcconf|pcalua|forfiles|msxsl|syncappvpublishingserver|presentationhost|infdefaultinstall|ieexec|diskshadow|esentutl|bitsadmin|certreq|mavinject)(\.exe)?\b|\bcertutil(\.exe)?\b(?![^\n]*-hashfile)|\bmsdt(\.exe)?\b|\bhh(\.exe)?\s|\b(csc|vbc|jsc|ilasm)(\.exe)?\s|\bwsl(\.exe)?\b|\bbash(\.exe)?\b|\bsh(\.exe)?\s+-c\b|\bconhost(\.exe)?\s+--headless\b|\bmsiexec\b[^\n]*(https?:|\\\\)", "narzędzie systemowe uruchamiające kod (rundll32, mshta, wscript…) — tego asystent nie używa", onlyWhenChanging: true),
            new Rule(RemoteCode, @"\b(powershell|pwsh|powershell_ise)(\.exe)?['""]?\s+[-/][a-z]|\bcmd(\.exe)?['""]?\s*/[ckr]\b", "uruchamianie drugiego PowerShella albo cmd — napisz polecenie wprost w tym PowerShellu", onlyWhenChanging: true),
            new Rule(RemoteCode, @"(\b(set-content|add-content|out-file|new-item|copy-item|move-item|rename-item|tee-object|ren|copy|move)\b[^\n]*|>>?\s*['""]?[^\s'""]*)\.(reg|hta|vbs|vbe|jse|wsf|wsh|scr|cpl|sct|inf|pif|lnk|url|msc|website|settingcontent-ms)\b", "tworzenie pliku, który Windows wykonuje (.reg, .vbs, .lnk…) — tego asystent nie robi", onlyWhenChanging: true),
            new Rule(RemoteCode, @"\b(python[0-9.]*|py|pythonw|perl|ruby|php|lua|node|deno|bun)(\.exe)?\s+(-c|-e|--eval|-r|-p|--print)\s", "kod podany w linii interpretera (python -c, node -e…) — zapisz skrypt plikiem i pokaż użytkownikowi"),
            new Rule(RemoteCode, @"%[^%\s]+:~-?\d+|\bcall\s+%|(^|\n)\s*%[a-z_0-9]+%\s|![a-z_0-9]+:~", "polecenie cmd składane ze zmiennych — napisz polecenie wprost"),

            // siec_biura — inne komputery
            new Rule(Office, @"\b(invoke-command|icm|new-pssession|nsn|enter-pssession|etsn|connect-pssession|get-wmiobject|gwmi|set-wmiinstance|remove-wmiobject|get-ciminstance|gcim|new-cimsession|set-ciminstance|remove-ciminstance|get-process|gps|stop-process|get-service|gsv|set-service|restart-service|stop-service|start-service|get-eventlog|get-winevent|get-hotfix|restart-computer|stop-computer|rename-computer|add-computer|remove-computer|copy-item|cpi|remove-item|ri|test-wsman|get-wsmaninstance|invoke-wsmanaction|connect-wsman|get-smbshare)\b[^\n;|]*\s-(computername|cn|session|cimsession)\b", "polecenie na innym komputerze w sieci"),
            new Rule(Office, @"\b(enter-pssession|etsn|new-pssession|nsn|connect-pssession|receive-pssession|new-cimsession|enter-pshostprocess)\b", "zdalna sesja na innym komputerze"),
            new Rule(Office, @"\b(psexec(64)?|paexec|remcom|winrs)(\.exe)?\b|\bwmic(\.exe)?\b[^\n]*/node|\bschtasks\b[^\n]*\s/s\s|\bsc(\.exe)?\s+\\\\|\breg(\.exe)?\s+\w+\s+\\\\|\bshutdown\b[^\n]*\s[/-]m\s|\bmsg(\.exe)?\s+\*", "zdalne uruchamianie na innych komputerach"),
            new Rule(Office, @"\\\\[^\\\s'""]+\\(admin\$|[a-z]\$|ipc\$|print\$)", "udział administracyjny innego komputera (C$, ADMIN$)"),
            new Rule(Office, @"\b(nmap|masscan|zmap|angryip\w*|portqry|nbtscan|arp-scan|psping|hping\d?)\b|\badvanced ip scanner\b|\bnet\.sockets\.tcpclient\b|\btcpclient\b|\.(connectasync|beginconnect)\s*\(", "skanowanie sieci albo łączenie z dowolnymi portami"),
            new Rule(Office, @"(?m)^(?=[^\n]*\b(test-netconnection|tnc|test-connection|ping|resolve-dnsname|nslookup)\b)[^\n]*\b\d{1,3}\s*\.\.\s*\d{1,5}\b", "przeglądanie wielu adresów albo portów (skanowanie)"),

            // niszczenie — uzupełnienie blokad PowerShellPolicy (ta działa tylko w narzędziu powershell)
            new Rule(Destroy, @"\bcipher(\.exe)?\s+/[we]\b|\b(vssadmin|wbadmin|bcdedit|reagentc|diskpart)(\.exe)?\b|\bdelete\s+shadows\b|\bresize\s+shadowstorage\b|\bclear-disk\b|\binitialize-disk\b|\bformat-volume\b|\bremove-partition\b|\bsdelete(64)?\b", "operacje na dyskach, kopiach zapasowych albo rozruchu"),
            new Rule(Destroy, @"\b(shutdown|logoff|tsdiscon)(\.exe)?['""]?\s+[/-][a-z?]|\b(shutdown|logoff)(\.exe)?['""]?\s*$|\b(stop|restart)-computer\b", "wyłączanie albo restart komputera"),
        };

        // -------------------------------------------------------------------------------------------------- zawsze pytać

        private static readonly Rule[] AskRules =
        {
            new Rule(Risky, @"\bwinget(\.exe)?\s+(install|upgrade|import|source\s+add|configure)\b|\bchoco(latey)?(\.exe)?\s+(install|upgrade|source)\b|\bscoop\s+(install|bucket)\b|\b(install|update|save)-(module|package|script|psresource)\b|\bregister-(psrepository|packagesource)\b|\bset-psrepository\b|\bmsiexec\b|\badd-appx(package|provisionedpackage)\b|\badd-appprovisionedpackage\b|\benable-windowsoptionalfeature\b|\badd-windowscapability\b|\binstall-windowsfeature\b|\bdism(\.exe)?\b[^\n]*/(enable|add)-|\b(pip3?|pipx)(\.exe)?\s+install\b|\bpython[0-9.]*(\.exe)?\s+-m\s+pip\s+install\b|\bnpm(\.cmd)?\s+(i|install|ci)\b|\byarn\s+add\b|\bdotnet\s+tool\s+install\b|\.(msi|msix|msixbundle|appx|appxbundle)\b", "instalacja programu albo modułu"),
            new Rule(Risky, @"\bwinget(\.exe)?\s+(uninstall|remove)\b|\bchoco(latey)?(\.exe)?\s+uninstall\b|\buninstall-(module|package|script|psresource)\b|\bremove-appx(package|provisionedpackage)\b|\bdisable-windowsoptionalfeature\b|\bremove-windowscapability\b|\b(pip3?|pipx)(\.exe)?\s+uninstall\b", "odinstalowanie programu albo modułu"),
            new Rule(Risky, @"\breg(\.exe)?\s+(add|delete|copy|unload)\b|\bregistry\]::|\bregistrykey\b|\.(createsubkey|deletesubkey|deletesubkeytree|deletevalue)\s*\(", "zmiana w rejestrze Windows"),
            new Rule(Risky, @"\b(start|stop|restart|suspend|resume|set)-service\b|\bsc(\.exe)?\s+(start|stop|pause|continue)\b|\bnet(1)?(\.exe)?\s+(start|stop|pause|continue)\s+\S", "uruchamianie albo zatrzymywanie usługi Windows"),
            new Rule(Risky, @"\bsetx(\.exe)?\b|\[(system\.)?environment\]::setenvironmentvariable\b|\bhkcu:\\environment\b", "trwała zmiana zmiennej środowiskowej"),
            new Rule(Risky, @"\b(stop-process|spps)\b|\btaskkill(\.exe)?\b|\.kill\s*\(|(^|[;|]\s*)kill\s", "zamykanie programu (niezapisana praca przepadnie)"),
            new Rule(Risky, @"\bnet(1)?(\.exe)?\s+use\b[^\n]*\\\\|\bnew-psdrive\b[^\n]*\\\\|\bnew-smbmapping\b", "podłączanie dysku sieciowego"),
            new Rule(Risky, @"security\.cryptography\.(aes\w*|rijndael\w*|tripledes\w*|des\b|rc2\w*|rsa\w*)|\bprotect-cmsmessage\b|\b7z(a|r)?(\.exe)?\s+a\b[^\n]*\s-p|\bgpg(\.exe)?\s+(-c|--symmetric|-e|--encrypt)\b", "szyfrowanie plików"),
            new Rule(Risky, @"\bmstsc(\.exe)?\b|\b(ssh|plink|putty)(\.exe)?\s", "połączenie zdalne z innym komputerem"),
            new Rule(Risky, @"-comobject\b", "obiekt COM"),
            new Rule(Risky, @"\bgit(\.exe)?\s+(push|remote\s+(add|set-url)|clone|fetch|pull)\b", "git wysyła albo pobiera dane z sieci"),
        };

        private static readonly HashSet<string> Shells = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "powershell", "pwsh", "powershell_ise", "cmd", "command", "wscript", "cscript", "mshta", "bash", "wsl", "sh", "zsh", "conhost",
        };

        /// <summary>
        /// Narzędzia, których asystent nie uruchamia w ogóle (wykonują kod, omijają ocenę albo służą do ataku). Narzędzia z odczytem
        /// przydatnym w pracy (netsh … show, net use, reg query, sc query, schtasks /query, icacls bez zmian) ocenia się po treści.
        /// </summary>
        private static readonly HashSet<string> SystemTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "rundll32", "regsvr32", "msbuild", "installutil", "regasm", "regsvcs", "cmstp", "odbcconf", "pcalua", "forfiles", "msxsl",
            "syncappvpublishingserver", "presentationhost", "infdefaultinstall", "ieexec", "diskshadow", "esentutl", "msdt", "hh",
            "certreq", "bitsadmin", "wmic", "regedit", "regedt32", "vssadmin", "wbadmin", "bcdedit", "takeown", "cmdkey", "vaultcmd", "runas",
            "psexec", "psexec64", "paexec", "winrs", "mavinject", "csc", "vbc", "jsc", "ilasm", "diskpart", "format", "cipher", "reagentc",
            "ntdsutil", "procdump", "procdump64", "sdelete", "ngrok", "cloudflared", "frpc", "chisel", "tailscale", "socat", "ncat", "netcat",
            "nc", "nc64", "gsudo", "sudo",
        };

        private static readonly HashSet<string> RemoteTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "anydesk", "teamviewer", "teamviewer_service", "rustdesk", "tvnviewer", "vncviewer", "mstsc", "quickassist", "msra", "ssh", "putty",
            "plink", "winscp", "filezilla",
        };

        /// <summary>Argumenty programu zdalnego dostępu, które go instalują albo otwierają na stałe (nie tylko uruchamiają).</summary>
        private static readonly Regex RemoteInstallArgs = new Regex(@"(--|/|-)(install|silent|unattended|set-password|password|start-with-win|autostart|service|remove-firstrun|grant|accept)|\b(install|setup)\b", Options, MatchTimeout);

        /// <summary>Programy do przesyłania plików poza komputer (sprawdzane jako wywołanie, nie słowo w tekście — „Z FTP od inwestora” to folder).</summary>
        private static readonly HashSet<string> TransferTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ftp", "tftp", "sftp", "scp", "pscp", "psftp", "winscp", "rclone", "azcopy", "gsutil", "megacmd", "mega-put",
        };

        /// <summary>Polecenia i programy, które otwierają adres (przeglądarka) — adres w nich przechodzi kontrolę pochodzenia.</summary>
        private static readonly HashSet<string> Launchers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "start-process", "saps", "start", "invoke-item", "ii", "explorer", "explorer.exe", "chrome", "chrome.exe", "msedge", "msedge.exe",
            "firefox", "firefox.exe", "iexplore", "iexplore.exe", "opera", "opera.exe", "brave", "brave.exe",
        };

        /// <summary>Polecenia zapisujące, przenoszące albo kasujące pliki.</summary>
        private static readonly HashSet<string> WriteCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "set-content", "sc", "add-content", "ac", "out-file", "remove-item", "ri", "rm", "del", "erase", "rd", "rmdir", "move-item", "mi", "mv",
            "move", "copy-item", "cpi", "cp", "copy", "new-item", "ni", "rename-item", "rni", "ren", "tee-object", "tee", "expand-archive",
            "compress-archive", "clear-content", "clc", "robocopy", "xcopy",
        };

        /// <summary>Ścieżka budowana od folderów profilu użytkownika (tam leżą Amadeusz, autostart i klucze).</summary>
        private static readonly Regex ProfileRootHint = new Regex(@"\$env:(appdata|localappdata|userprofile|programdata|homepath|homedrive|public|allusersprofile)\b|\$home\b|(^|[\s'""(])~[\\/]|getfolderpath|specialfolder|\[environment\]::", Options, MatchTimeout);

        /// <summary>Operacja na całym drzewie folderów.</summary>
        private static readonly Regex RecursionHint = new Regex(@"\s-r(e|ec|ecu|ecur|ecurs|ecurse)?\b|\s-depth\b|\brobocopy\b|\bxcopy\b[^\n]*\s/[se]\b|\b(rd|rmdir)\b[^\n]*\s/s\b|\bdel\b[^\n]*\s/s\b|\btar(\.exe)?\s|\bcompress-archive\b", Options, MatchTimeout);

        private static readonly Regex PythonNetwork = new Regex(@"\brequests\.\w+\(|\burllib\b|\bhttp\.client\b|\bhttpx\b|\baiohttp\b|\bsocket\b|\bwebbrowser\b|\bftplib\b|\bsmtplib\b|\bparamiko\b", Options, MatchTimeout);
        private static readonly Regex RegistryPath = new Regex(@"\bhk(cu|lm|cr|u|cc):|\bhkey_[a-z_]+|registry::", Options, MatchTimeout);
        private static readonly Regex ItemWrite = new Regex(@"\b(new|set|remove|rename|copy|move|clear)-item(property)?\b|\.setvalue\s*\(", Options, MatchTimeout);

        private static readonly HashSet<string> Interpreters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "python", "python3", "py", "pythonw", "perl", "ruby", "php", "lua", "node", "deno", "bun", "java", "javaw",
        };

        /// <summary>Nie uruchamiać i nie zapisywać: formaty, które Windows wykonuje, a których asystent nie potrzebuje.</summary>
        private static readonly HashSet<string> BlockedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".reg", ".hta", ".vbs", ".vbe", ".jse", ".wsf", ".wsh", ".scr", ".cpl", ".sct", ".inf", ".pif", ".msc", ".gadget", ".application",
            ".settingcontent-ms", ".library-ms", ".searchconnector-ms",
        };

        private static readonly HashSet<string> ScriptExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".ps1", ".psm1", ".psd1", ".bat", ".cmd", ".py", ".pyw", ".js", ".mjs", ".vb", ".rb", ".pl", ".php", ".lua", ".sh",
        };

        private static readonly HashSet<string> ShortcutExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".lnk", ".url", ".appref-ms", ".website",
        };

        private static readonly HashSet<string> InstallerExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".msi", ".msp", ".msix", ".msixbundle", ".appx", ".appxbundle",
        };

        private static readonly HashSet<string> ExecutableExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".com",
        };

        private static readonly HashSet<string> SecretFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "klucze.json", ".git-credentials", ".netrc", "_netrc", ".npmrc", ".pypirc", "login data", "cookies", "local state", "web data",
            "logins.json", "key3.db", "key4.db", "ntuser.dat", "authorized_keys",
        };

        private static readonly Regex SecretFilePattern = new Regex(
            @"^(id_(rsa|dsa|ecdsa|ed25519)(\.pub)?|.*\.(pem|pfx|p12|ppk|kdbx|kdb|jks|keystore|key)|.*\.local\.json|\.env(\..+)?)$", Options, MatchTimeout);

        private static readonly Regex ProtectedName = new Regex(@"[\\/]lokalneai\b|\blokalneai[\\/]|\blokalneai(\.\w+)?\.exe\b|\blokalneai\.eval\b|\blokaln~\d|\bamadeu~\d|(^|[\\/'""\s])\.amadeusz([\\/'""\s]|$)|\bamadeusz(\.stary)?\.exe\b", Options, MatchTimeout);
        private static readonly Regex ModelEndpoint = new Regex(@"(?:(https?)://)?((?:localhost|127\.\d{1,3}\.\d{1,3}\.\d{1,3}|\[?::1\]?|0\.0\.0\.0|\[::\]|[a-z0-9-]+(?:\.[a-z0-9-]+)*))\s*:\s*(1143[4-6])\b(/[^\s'""`)\]]*)?", Options, MatchTimeout);
        private static readonly Regex AnyUrl = new Regex(@"https?://[^\s'""`<>)\]}]+", Options, MatchTimeout);
        private static readonly Regex HostPortPath = new Regex(@"\b[a-z0-9.-]+:\d{2,5}/[^\s'""`<>)\]}]*", Options, MatchTimeout);
        private static readonly Regex ExtraRequestData = new Regex(@"\s-(headers|websession|sessionvariable|credential|proxy|proxycredential|certificate|certificatethumbprint|customheaders)\b|\s-useragent\s+(""[^""]*\$|[^\s'""]*\$|\()|\bcurl(\.exe)?\b[^\n]*\s(-h\b|--header|-u\b|--user|-b\b|--cookie|-x\s+\S+:\d|--proxy)", Options, MatchTimeout);
        private static readonly Regex NeedsInternet = new Regex(@"\bwinget(\.exe)?\s+(install|upgrade|import|source)\b|\bchoco(latey)?(\.exe)?\s+(install|upgrade)\b|\bscoop\s+install\b|\b(install|update|save|find)-(module|package|script|psresource)\b|\b(pip3?|pipx)(\.exe)?\s+install\b|\bpython[0-9.]*(\.exe)?\s+-m\s+pip\s+install\b|\bnpm(\.cmd)?\s+(i|install|ci)\b|\bgit(\.exe)?\s+(clone|fetch|pull|push|ls-remote)\b|\bdotnet\s+tool\s+install\b", Options, MatchTimeout);
        private static readonly Regex AdminShare = new Regex(@"^\\\\[^\\]+\\(admin\$|[a-z]\$|ipc\$|print\$)(\\|$)", Options, MatchTimeout);
        private static readonly Regex AutostartPath = new Regex(@"\\start menu\\programs\\startup(\\|$)|\\(windows)?powershell\\[^\\]*profile\.ps1$|\\microsoft\\(word\\startup|excel\\xlstart)(\\|$)|\\microsoft\\templates\\normal\.dotm$", Options, MatchTimeout);
        private static readonly Regex LinkCreation = new Regex(@"-itemtype\s+['""]?(symboliclink|junction|hardlink)\b|\bmklink\b|\bfsutil(\.exe)?\s+hardlink\b|\bcreatesymboliclink\b", Options, MatchTimeout);
        private static readonly Regex NetworkCommand = new Regex(@"\b(invoke-webrequest|iwr|invoke-restmethod|irm|curl|wget|start-bitstransfer)\b|\bnet\.(webclient|webrequest|httpwebrequest)\b|\bhttpclient\b|\bdownload(string|file|data)\b|\bxmlhttp\b|\bwinhttp\b", Options, MatchTimeout);
        private static readonly Regex DangerousFormula = new Regex(@"\b(webservice|filterxml|image|rtd|call|register(\.id)?|exec|usługa\.sieciowa|usługa\.sieci\.web|filtruj\.xml|obraz|wywołaj|rejestruj(\.id)?)\s*\(|\|\s*'[^']*'\s*!|^[=+\-@]\s*[a-z0-9_.]+\s*\|", Options, MatchTimeout);
        /// <summary>Człon ścieżki adresu, który wygląda na dane (base64, długi ciąg szesnastkowy), a nie na nazwę strony z myślnikami.</summary>
        private static readonly Regex DataLikeSegment = new Regex(@"^([A-Za-z0-9+=_]{40,}|[0-9a-fA-F]{32,})$", RegexOptions.CultureInvariant, MatchTimeout);
        private static readonly Regex Concatenation = new Regex(@"(['""])\s*\+\s*(['""])", RegexOptions.CultureInvariant, MatchTimeout);
        private static readonly Regex CharCodes = new Regex(@"\[char\]\s*\(?\s*(0x)?[0-9a-f]+", Options, MatchTimeout);
        private static readonly Regex ProgramFile = new Regex(@"\.(exe|msi|msix|ps1|psm1|bat|cmd|vbs|vbe|js|jse|hta|scr|dll|jar|py|pyw|reg|lnk|cpl)\b", Options, MatchTimeout);

        private static readonly string[] ReadOnlyModelPaths = { "", "/", "/api/tags", "/api/ps", "/api/version", "/lokalneai/info", "/v1/models" };

        private static readonly HashSet<string> NameQueryCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "resolve-dnsname", "nslookup", "nslookup.exe", "ping", "ping.exe", "test-connection", "test-netconnection", "tnc", "tracert", "tracert.exe",
            "pathping", "pathping.exe",
        };

        /// <summary>Polecenia czytające treść albo pakujące pliki (odczyt z gwiazdką albo -Recurse z folderu z kluczami — blokada).</summary>
        private static readonly HashSet<string> ContentCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "get-content", "gc", "cat", "type", "select-string", "sls", "copy-item", "cp", "copy", "cpi", "compress-archive", "import-csv",
            "import-clixml", "format-hex", "fhx", "findstr", "robocopy", "xcopy", "tar",
        };

        private static readonly HashSet<string> WebCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "invoke-webrequest", "iwr", "invoke-restmethod", "irm", "curl", "curl.exe", "wget", "wget.exe", "start-bitstransfer",
        };

        private static readonly HashSet<string> SafeTypeAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "system.io.compression", "system.io.compression.filesystem", "system.drawing", "system.web", "system.windows.forms", "presentationcore",
            "presentationframework", "windowsbase", "system.xml.linq", "system.data",
        };

        // -------------------------------------------------------------------------------------------------- skrypty

        /// <summary>Ocena skryptu (PowerShell, cmd, Python…) przed wykonaniem albo przed zapisem pliku skryptu.</summary>
        internal static SafetyVerdict CheckScript(string script, SafetyScope scope, ScriptLanguage language)
        {
            if (string.IsNullOrWhiteSpace(script))
            {
                return SafetyVerdict.Allowed;
            }

            if (script.Length > MaxScriptChars)
            {
                return SafetyVerdict.Block(RemoteCode, "polecenie ma " + script.Length + " znaków — za długie, żeby je sprawdzić (najwyżej " + MaxScriptChars
                    + "). Podziel je na krótsze.");
            }

            return Guarded(() => CheckScriptCore(script, scope, language));
        }

        /// <summary>Przekroczony limit czasu wzorca = blokada (nie da się sprawdzić — nie przepuszczamy).</summary>
        private static SafetyVerdict Guarded(Func<SafetyVerdict> check)
        {
            try
            {
                return check();
            }
            catch (RegexMatchTimeoutException)
            {
                return SafetyVerdict.Block(RemoteCode, "polecenia nie udało się sprawdzić w rozsądnym czasie — napisz je prościej.");
            }
        }

        private static SafetyVerdict CheckScriptCore(string script, SafetyScope scope, ScriptLanguage language)
        {
            // Komentarze nic nie wykonują — „# Skrypt PowerShell: …” nie może dać fałszywego alarmu.
            var code = StripComments(script, language);
            var text = Normalize(code, lowerCase: true);
            var cased = Normalize(code, lowerCase: false);
            var facts = language == ScriptLanguage.PowerShell ? PowerShellPolicy.Inspect(code) : new PowerShellPolicy.ScriptFacts();
            var changing = !IsReadOnly(code, scope, language, facts);

            foreach (var rule in BlockRules)
            {
                if ((changing || !rule.OnlyWhenChanging) && (!rule.PowerShellOnly || language == ScriptLanguage.PowerShell)
                    && rule.Pattern.IsMatch(rule.CaseSensitive ? cased : text))
                {
                    return SafetyVerdict.Block(rule.Category, rule.Reason + ".");
                }
            }

            if (CharCodes.Matches(text).Count >= 3)
            {
                return SafetyVerdict.Block(RemoteCode, "tekst składany z kodów znaków — napisz polecenie wprost.");
            }

            if (!scope.Internet && NeedsInternet.IsMatch(text))
            {
                return SafetyVerdict.Block(Leak, "dostęp do internetu jest wyłączony (przełącznik „Odblokuj dostęp do internetu” albo umiejętność bez internetu) — instalacje i git sięgają do sieci.");
            }

            if (LinkCreation.IsMatch(text))
            {
                return SafetyVerdict.Block(Self, "dowiązania (symlink, junction, hardlink) mogą przekierować zapis do folderów chronionych — tego asystent nie tworzy.");
            }

            if (facts.Invocations.Any(i => i.Name.Equals("sal", StringComparison.OrdinalIgnoreCase) || i.Name.Equals("nal", StringComparison.OrdinalIgnoreCase)))
            {
                return SafetyVerdict.Block(RemoteCode, "aliasy podmieniające polecenia — napisz polecenie wprost.");
            }

            var secrets = CheckSecretArguments(facts, scope) ?? CheckPrivateArguments(facts, scope);
            if (secrets != null)
            {
                return secrets;
            }

            var addType = CheckAddType(text);
            if (addType != null)
            {
                return addType;
            }

            if (language == ScriptLanguage.Python)
            {
                var python = CheckPython(text);
                if (python.Kind == SafetyVerdictKind.Block)
                {
                    return python;
                }
            }

            if (facts.Dynamic != null)
            {
                return SafetyVerdict.Block(RemoteCode, facts.Dynamic + " — napisz ścieżkę albo nazwę programu wprost, bez zmiennych.");
            }

            var self = CheckSelfReferences(text, facts, scope, changing);
            if (self.Kind == SafetyVerdictKind.Block)
            {
                return self;
            }

            var network = CheckNetwork(text, facts, scope, language);
            if (network.Kind == SafetyVerdictKind.Block)
            {
                return network;
            }

            var verdict = SafetyVerdict.Stricter(self, network);
            foreach (var invocation in facts.Invocations)
            {
                var found = CheckInvocation(invocation, scope);
                if (found.Kind == SafetyVerdictKind.Block)
                {
                    return found;
                }

                verdict = SafetyVerdict.Stricter(verdict, found);
            }

            if (verdict.Kind == SafetyVerdictKind.AlwaysAsk || !changing)
            {
                return verdict; // reguły „zawsze pytaj” dotyczą zmian — samo czytanie (np. lista *.msi) idzie zwykłą drogą
            }

            foreach (var rule in AskRules)
            {
                if (rule.Pattern.IsMatch(text))
                {
                    return SafetyVerdict.Ask(rule.Category, rule.Reason + ".");
                }
            }

            if (RegistryPath.IsMatch(text) && ItemWrite.IsMatch(text))
            {
                return SafetyVerdict.Ask(Risky, "zmiana w rejestrze Windows.");
            }

            if (ProfileRootHint.IsMatch(text) && facts.Invocations.Any(i => WriteCommands.Contains(i.Name) && i.Arguments.Any(a => a.Dynamic && !a.Text.StartsWith("-", StringComparison.Ordinal))))
            {
                return SafetyVerdict.Ask(Risky, "zapis, przeniesienie albo kasowanie w ścieżce budowanej ze zmiennych od folderu profilu — sprawdź, dokąd trafi.");
            }

            return language == ScriptLanguage.Python ? CheckPython(text) : SafetyVerdict.Allowed;
        }

        /// <summary>
        /// Każdy dosłowny argument (także w skrypcie, który tylko czyta): plik z hasłami albo kluczami — blokada; odczyt z gwiazdką albo
        /// rekurencyjnie z folderu, w którym są sekrety — blokada.
        /// </summary>
        private static SafetyVerdict CheckSecretArguments(PowerShellPolicy.ScriptFacts facts, SafetyScope scope)
        {
            foreach (var invocation in facts.Invocations)
            {
                var content = ContentCommands.Contains(invocation.Name);
                var recurse = content && (invocation.Arguments.Any(a => IsRecurseSwitch(a.Text)) || RecursiveTools.Contains(invocation.Name));
                foreach (var argument in invocation.Arguments.Where(a => !a.Dynamic && !a.Text.StartsWith("-", StringComparison.Ordinal) || a.Text.IndexOf(':') > 1))
                {
                    var full = scope.Resolve(argument.Text);
                    if (full == null)
                    {
                        continue;
                    }

                    if (IsSecret(full, scope))
                    {
                        return SafetyVerdict.Block(Secrets, "plik albo folder z hasłami lub kluczami (" + full + ") — tego asystent nie czyta, nie kopiuje i nie wysyła.");
                    }

                    if (content && (recurse || argument.Text.IndexOfAny(new[] { '*', '?' }) >= 0)
                        && scope.Secrets.Any(secret => IsInside(secret, new[] { Slash(full) })))
                    {
                        return SafetyVerdict.Block(Secrets, "odczyt całego folderu " + full + ", w którym są hasła albo klucze — wskaż konkretny plik.");
                    }
                }
            }

            return null;
        }

        private static bool IsRecurseSwitch(string text)
            => text.Length >= 2 && text.StartsWith("-r", StringComparison.OrdinalIgnoreCase) && "-recurse".StartsWith(text.ToLowerInvariant(), StringComparison.Ordinal)
               || text.Equals("/s", StringComparison.OrdinalIgnoreCase) || text.Equals("/e", StringComparison.OrdinalIgnoreCase)
               || text.Equals("/mir", StringComparison.OrdinalIgnoreCase);

        /// <summary>Narzędzia, które same z siebie biorą całe drzewo folderu.</summary>
        private static readonly HashSet<string> RecursiveTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "compress-archive", "robocopy", "tar", "tar.exe",
        };

        /// <summary>
        /// Czy skrypt tylko czyta: ocena <see cref="PowerShellPolicy"/> bez ograniczenia folderów, a do tego natywne polecenia, które
        /// same nic nie zmieniają (<c>reg query</c>, <c>netsh … show</c>, <c>sc query</c>, <c>schtasks /query</c>, <c>git status</c>…).
        /// </summary>
        private static bool IsReadOnly(string script, SafetyScope scope, ScriptLanguage language, PowerShellPolicy.ScriptFacts facts)
        {
            if (language != ScriptLanguage.PowerShell)
            {
                return false;
            }

            var analysis = AnalyzeAnywhere(script, scope);
            if (analysis.Verdict == ConfirmationKind.None)
            {
                return true;
            }

            if (analysis.Verdict != ConfirmationKind.Ask || analysis.Warnings.Count > 0)
            {
                return false;
            }

            // Pytanie tylko dlatego, że w skrypcie są natywne programy — wystarczy, że każdy z nich jest w wersji „tylko odczyt”.
            foreach (var reason in analysis.AskReasons)
            {
                if (!reason.StartsWith("polecenie ", StringComparison.Ordinal))
                {
                    return false;
                }

                var name = reason.Substring("polecenie ".Length);
                var invocations = facts.Invocations.Where(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
                if (invocations.Count == 0 || !invocations.All(ReadOnlyNative))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Natywne polecenie, które w tej postaci tylko czyta.</summary>
        private static bool ReadOnlyNative(PowerShellPolicy.ScriptInvocation invocation)
        {
            var name = Path.GetFileNameWithoutExtension(invocation.Name).ToLowerInvariant();
            var args = invocation.Arguments.Select(a => a.Text.ToLowerInvariant()).ToList();
            var first = args.FirstOrDefault() ?? "";
            if (invocation.Arguments.Any(a => a.Dynamic))
            {
                return false;
            }

            switch (name)
            {
                case "whoami":
                case "hostname":
                case "systeminfo":
                case "tasklist":
                case "getmac":
                case "driverquery":
                case "ver":
                case "quser":
                case "ping":
                case "tracert":
                case "pathping":
                case "nslookup":
                case "where":
                case "tree":
                    return true;
                case "reg":
                    return first == "query";
                case "netsh":
                    return args.Contains("show") && !args.Any(a => a == "set" || a == "add" || a == "delete" || a == "reset" || a == "import" || a == "export" || a == "install" || a == "uninstall");
                case "sc":
                    return first == "query" || first == "queryex" || first == "qc" || first == "qdescription" || first == "qfailure" || first == "getdisplayname" || first == "getkeyname";
                case "schtasks":
                    return first == "/query";
                case "ipconfig":
                    return args.Count == 0 || first == "/all" || first == "/displaydns";
                case "route":
                    return first == "print";
                case "arp":
                    return first == "-a" || first == "/a";
                case "net":
                case "net1":
                    return first == "view" || first == "config" || first == "statistics" || (args.Count == 1 && (first == "use" || first == "user" || first == "localgroup" || first == "share" || first == "accounts"));
                case "git":
                    return first == "status" || first == "log" || first == "diff" || first == "show" || first == "rev-parse" || first == "describe" || first == "ls-files"
                           || first == "blame" || (first == "branch" && !args.Any(a => a.StartsWith("-d", StringComparison.Ordinal) || a == "-m" || a == "-c"))
                           || (first == "remote" && args.Count <= 2 && (args.Count == 1 || args[1] == "-v"));
                case "wevtutil":
                    return first == "qe" || first == "gl" || first == "el" || first == "gp" || first == "enum-logs" || first == "query-events";
                case "icacls":
                    return !args.Any(a => a.StartsWith("/", StringComparison.Ordinal));
                case "certutil":
                    return first == "-hashfile";
                case "auditpol":
                    return first == "/get";
                default:
                    return false;
            }
        }

        /// <summary>Ocena <see cref="PowerShellPolicy"/> z dozwolonymi wszystkimi dyskami — tylko „czyta czy zmienia”, bez sprawdzania folderów.</summary>
        private static CommandAnalysis AnalyzeAnywhere(string script, SafetyScope scope)
        {
            var drives = new List<string>();
            try
            {
                drives.AddRange(DriveInfo.GetDrives().Select(d => d.Name));
            }
            catch (IOException)
            {
                drives.Add("C:\\");
            }
            catch (UnauthorizedAccessException)
            {
                drives.Add("C:\\");
            }

            return PowerShellPolicy.Analyze(script, scope.WorkingFolder, drives);
        }

        private static SafetyVerdict CheckAddType(string text)
        {
            foreach (Match match in Regex.Matches(text, @"\badd-type\b([^\n;|]*)", Options))
            {
                var rest = match.Groups[1].Value;
                var assembly = Regex.Match(rest, @"-assemblyname\s+['""]?([\w.]+)", Options);
                if (!assembly.Success || !SafeTypeAssemblies.Contains(assembly.Groups[1].Value)
                    || Regex.IsMatch(rest, @"-(typedefinition|memberdefinition|path|literalpath|outputassembly|compilerparameters|referencedassemblies)\b", Options))
                {
                    return SafetyVerdict.Block(RemoteCode, "Add-Type kompiluje albo ładuje kod w locie — tego asystent nie robi.");
                }
            }

            return null;
        }

        /// <summary>Ślady folderów Amadeusza i folderów chronionych; zmiana w nich (albo w folderze bieżącym wewnątrz nich) — blokada.</summary>
        private static SafetyVerdict CheckSelfReferences(string text, PowerShellPolicy.ScriptFacts facts, SafetyScope scope, bool changing)
        {
            if (!changing)
            {
                return SafetyVerdict.Allowed;
            }

            // Adresy (http://127.0.0.1:11435/lokalneai/info) to nie ścieżki do plików — sprawdza je CheckNetwork.
            var paths = HostPortPath.Replace(AnyUrl.Replace(text, " "), " ");
            if (ProtectedName.IsMatch(paths) || scope.MentionsProtectedFolder(paths))
            {
                return SafetyVerdict.Block(Self, "zmiana plików samego Amadeusza (program, umiejętności, ustawienia, rozmowy) albo folderu chronionego. Czytać je wolno; zmienia je tylko użytkownik.");
            }

            var working = scope.WorkingFolder == null ? null : scope.Resolve(scope.WorkingFolder);
            if (working != null && IsInside(working, scope.Protected))
            {
                return SafetyVerdict.Block(Self, "folder bieżący (" + working + ") należy do samego Amadeusza albo jest chroniony — tu asystent tylko czyta.");
            }

            var targets = new List<string>();
            foreach (var argument in facts.Invocations.SelectMany(i => i.Arguments.Where(a => !a.Dynamic).Select(a => a.Text).Concat(new[] { i.Name })))
            {
                var full = scope.Resolve(argument);
                if (full == null)
                {
                    continue;
                }

                if (IsInside(full, scope.Protected))
                {
                    return SafetyVerdict.Block(Self, "zmiana w folderze chronionym: " + full + ". Czytać wolno; zmienia tylko użytkownik.");
                }

                targets.Add(full);
            }

            // Operacja na całym drzewie folderu, w którym leży folder Amadeusza (np. zamiana tekstu we wszystkich .md w C:\ODS), zmieniłaby
            // też jego umiejętności i wiedzę.
            if (RecursionHint.IsMatch(text))
            {
                foreach (var folder in targets.Concat(working == null ? Enumerable.Empty<string>() : new[] { working }))
                {
                    var inside = scope.Protected.FirstOrDefault(p => IsInside(p, new[] { Slash(folder) }) && !string.Equals(Slash(p), Slash(folder), StringComparison.OrdinalIgnoreCase));
                    if (inside != null)
                    {
                        return SafetyVerdict.Block(Self, "operacja na całym folderze " + folder + " objęłaby też pliki samego Amadeusza (" + inside.TrimEnd('\\')
                            + ") — wskaż węższy folder albo pomiń ten.");
                    }
                }
            }

            return SafetyVerdict.Allowed;
        }

        /// <summary>Sieć: model i bramy (11434–11436), adresy w sieci biura, internet wyłączony, adresy spoza rozmowy, pobierz-i-uruchom.</summary>
        private static SafetyVerdict CheckNetwork(string text, PowerShellPolicy.ScriptFacts facts, SafetyScope scope, ScriptLanguage language)
        {
            var hasNetwork = NetworkCommand.IsMatch(text) || facts.Invocations.Any(i => WebCommands.Contains(i.Name))
                             || language == ScriptLanguage.Python && PythonNetwork.IsMatch(text);
            if (hasNetwork && ExtraRequestData.IsMatch(text))
            {
                return SafetyVerdict.Block(Leak, "zapytanie sieciowe z własnymi nagłówkami, poświadczeniami albo proxy — pobieraj zwykłym Invoke-WebRequest z adresem.");
            }

            foreach (var invocation in facts.Invocations.Where(i => NameQueryCommands.Contains(i.Name)))
            {
                var host = invocation.Arguments.FirstOrDefault(a => !a.Text.StartsWith("-", StringComparison.Ordinal));
                if (host != null && !host.Dynamic && host.Text.Split('.').Any(label => label.Length >= 25) || host != null && host.Text.Length > 70)
                {
                    return SafetyVerdict.Block(Leak, "nazwa " + Text.Shorten(host.Text, 50) + " wygląda na dane, nie na adres serwera.");
                }
            }
            foreach (Match match in ModelEndpoint.Matches(text))
            {
                var path = (match.Groups[4].Value ?? "").TrimEnd('/', '.', ',', ';');
                if (!ReadOnlyModelPaths.Contains(path, StringComparer.OrdinalIgnoreCase) || Regex.IsMatch(text, @"-(method|body|infile|form)\b", Options))
                {
                    return SafetyVerdict.Block(Self, "wołanie modelu albo bramy Amadeusza (port " + match.Groups[3].Value + ") z pominięciem Amadeusza — wolno tylko odczytać stan (/lokalneai/info, /api/tags, /api/ps, /api/version).");
                }
            }

            foreach (var invocation in facts.Invocations)
            {
                if ((NameQueryCommands.Contains(invocation.Name) || WebCommands.Contains(invocation.Name))
                    && invocation.Arguments.FirstOrDefault(a => !a.Text.StartsWith("-", StringComparison.Ordinal)) is PowerShellPolicy.ScriptArgument target && target.Dynamic)
                {
                    return SafetyVerdict.Block(Leak, invocation.Name + " z adresem budowanym ze zmiennych (" + Text.Shorten(target.Text, 40) + ") — podaj adres wprost.");
                }
            }

            // Sprawdzanie portu na innym komputerze (Test-NetConnection -Port) to rozpoznanie sieci — pojedynczo za zgodą, w pętli zablokowane wyżej.
            var probe = PortProbeTarget(facts);
            var portAsk = probe == null ? SafetyVerdict.Allowed
                : SafetyVerdict.Ask(Office, "sprawdzanie portu na innym komputerze (" + Text.Shorten(probe, 60) + ") — to rozpoznanie sieci biura; zrób to tylko na prośbę użytkownika.");

            // Adresy, z którymi coś się dzieje: w skrypcie sieciowym — wszystkie; poza nim — tylko te przekazane programom (przeglądarka,
            // Start-Process). Adres w zwykłym tekście (notatka, przestrzeń nazw XML) niczego nie wysyła.
            var urls = hasNetwork
                ? AnyUrl.Matches(text).Cast<Match>().Select(m => m.Value).ToList()
                : facts.Invocations.Where(i => Launchers.Contains(i.Name) || LooksLikeProgramName(i.Name, scope))
                    .SelectMany(i => i.Arguments.SelectMany(a => AnyUrl.Matches(a.Text).Cast<Match>().Select(m => m.Value))).ToList();
            var ask = SafetyVerdict.Allowed;
            foreach (var found in urls)
            {
                if (!Uri.TryCreate(found.TrimEnd('.', ',', ';', ':'), UriKind.Absolute, out var uri))
                {
                    continue;
                }

                if (IsLoopback(uri.Host))
                {
                    continue; // port i ścieżki sprawdza ModelEndpoint; inne usługi na tym komputerze — zwykła ocena
                }

                if (IsLocalNetworkHost(uri.Host))
                {
                    return SafetyVerdict.Block(Office, "sięganie do komputera albo usługi w sieci biura (" + uri.Host + ") — przez PowerShell tego asystent nie robi.");
                }

                if (!scope.Internet)
                {
                    return SafetyVerdict.Block(Leak, "dostęp do internetu jest wyłączony (przełącznik „Odblokuj dostęp do internetu” albo umiejętność bez internetu).");
                }

                if (!scope.IsKnownUrl(uri.ToString()))
                {
                    return SafetyVerdict.Block(Leak, "adres " + Text.Shorten(uri.ToString(), 80) + " nie pochodzi od użytkownika ani z wyników wyszukiwania — asystent nie wysyła zapytań i nie otwiera adresów, które sam złożył.");
                }

                ask = SafetyVerdict.Ask(Risky, (hasNetwork ? "pobieranie z internetu (" : "otwieranie strony (") + uri.Host + ").");
            }

            if (hasNetwork && facts.Invocations.Any(IsExecution))
            {
                return SafetyVerdict.Block(RemoteCode, "pobieranie z sieci i uruchamianie w jednym poleceniu — tego asystent nie robi.");
            }

            if (hasNetwork && (ProgramFile.IsMatch(Regex.Replace(AnyUrl.Replace(text, " "), @"\b(curl|wget)\.exe\b", "$1", Options))
                               || AnyUrl.Matches(text).Cast<Match>().Any(m => Uri.TryCreate(m.Value, UriKind.Absolute, out var u) && ProgramFile.IsMatch(u.AbsolutePath))))
            {
                return SafetyVerdict.Block(RemoteCode, "pobieranie programów albo skryptów z internetu — programy instaluje użytkownik.");
            }

            if (hasNetwork && !scope.Internet && !ModelEndpoint.IsMatch(text))
            {
                return SafetyVerdict.Block(Leak, "dostęp do internetu jest wyłączony.");
            }

            if (hasNetwork && ask.Kind == SafetyVerdictKind.Allow && !ModelEndpoint.IsMatch(text))
            {
                return SafetyVerdict.Stricter(SafetyVerdict.Ask(Risky, "polecenie sieciowe."), portAsk);
            }

            return SafetyVerdict.Stricter(ask, portAsk);
        }

        /// <summary>
        /// Cel sprawdzania portu (<c>Test-NetConnection … -Port</c>, <c>-CommonTCPPort</c>, <c>Test-Connection -TcpPort</c>) na komputerze
        /// innym niż ten; null = brak takiego wywołania albo tylko ten komputer.
        /// </summary>
        private static string PortProbeTarget(PowerShellPolicy.ScriptFacts facts)
        {
            foreach (var invocation in facts.Invocations)
            {
                var name = invocation.Name.ToLowerInvariant();
                var args = invocation.Arguments;
                bool IsPortSwitch(string text) => name == "test-connection" || name == "test-connection.exe"
                    ? Regex.IsMatch(text, @"^-t(c|cp|cpp|cppo|cppor|cpport)(:.*)?$", Options)
                    : Regex.IsMatch(text, @"^-(p|po|por|port|comm|commo|common\w*)(:.*)?$", Options);
                if (name != "test-netconnection" && name != "tnc" && name != "test-connection" || !args.Any(a => IsPortSwitch(a.Text)))
                {
                    continue;
                }

                var hosts = new List<string>();
                for (var i = 0; i < args.Count; i++)
                {
                    var text = args[i].Text;
                    if (text.StartsWith("-", StringComparison.Ordinal))
                    {
                        if (Regex.IsMatch(text, @"^-(c|co|com|comp|compu|comput|compute|computer|computern\w*|cn|targetname|t)$", Options) && i + 1 < args.Count)
                        {
                            hosts.Add(args[++i].Text);
                        }
                        else if (text.IndexOf(':') < 0 && i + 1 < args.Count && !args[i + 1].Text.StartsWith("-", StringComparison.Ordinal))
                        {
                            i++; // wartość innego parametru (np. -Port 445, -InformationLevel Quiet)
                        }

                        continue;
                    }

                    if (!Regex.IsMatch(text, @"^\d+$"))
                    {
                        hosts.Add(text);
                    }
                }

                if (hosts.Count == 0)
                {
                    return "internetbeacon.msedge.net";
                }

                var remote = hosts.FirstOrDefault(h => !IsLoopback(h.Trim('\'', '"', '[', ']')) && !string.Equals(h, ".", StringComparison.Ordinal)
                                                        && !string.Equals(h, Environment.MachineName, StringComparison.OrdinalIgnoreCase));
                if (remote != null)
                {
                    return remote;
                }
            }

            return null;
        }

        /// <summary>Wywołanie, które uruchamia program albo skrypt (nie zwykłe polecenie PowerShella).</summary>
        private static bool IsExecution(PowerShellPolicy.ScriptInvocation invocation)
        {
            var name = invocation.Name.ToLowerInvariant();
            if (WebCommands.Contains(name))
            {
                return false; // curl.exe sam w sobie pobiera, nie uruchamia
            }

            if (name == "start-process" || name == "saps" || name == "start" || name == "invoke-item" || name == "ii" || name == "msiexec"
                || name == "msiexec.exe" || invocation.ViaCallOperator)
            {
                return true;
            }

            var extension = Path.GetExtension(name);
            return name.IndexOfAny(new[] { '\\', '/' }) >= 0 || ExecutableExtensions.Contains(extension) || ScriptExtensions.Contains(extension)
                   || Shells.Contains(name) || SystemTools.Contains(name) || Interpreters.Contains(name);
        }

        /// <summary>Nazwa wygląda na program (ścieżka, rozszerzenie albo znany program), a nie na polecenie PowerShella.</summary>
        private static bool LooksLikeProgramName(string name, SafetyScope scope)
            => name.IndexOfAny(new[] { '\\', '/' }) >= 0 || Path.GetExtension(name).Length > 1 || Shells.Contains(name) || SystemTools.Contains(name)
               || Interpreters.Contains(name) || TransferTools.Contains(name) || RemoteTools.Contains(name) || scope.SelfPrograms.Contains(name);

        /// <summary>Program wywołany w skrypcie: sam Amadeusz, interpretery, narzędzia systemowe, skrypty z plików.</summary>
        private static SafetyVerdict CheckInvocation(PowerShellPolicy.ScriptInvocation invocation, SafetyScope scope)
        {
            var name = invocation.Name;
            var lower = name.ToLowerInvariant();
            if (lower == "set-alias" || lower == "new-alias" || lower == "sal" || lower == "nal")
            {
                return SafetyVerdict.Block(RemoteCode, "aliasy podmieniające polecenia — napisz polecenie wprost.");
            }
            if (lower == "start-process" || lower == "saps" || lower == "start")
            {
                var program = ProgramArgument(invocation);
                if (program == null)
                {
                    return SafetyVerdict.Allowed;
                }

                if (program.Dynamic)
                {
                    return SafetyVerdict.Block(RemoteCode, "Start-Process z programem ze zmiennej — podaj program wprost.");
                }

                var arguments = string.Join(" ", invocation.Arguments.Where(a => a != program).Select(a => a.Text));
                return CheckProgram(program.Text, arguments, scope, fromScript: true);
            }

            if (lower == "invoke-item" || lower == "ii")
            {
                var target = invocation.Arguments.FirstOrDefault(a => !a.Text.StartsWith("-", StringComparison.Ordinal));
                return target == null ? SafetyVerdict.Allowed : target.Dynamic
                    ? SafetyVerdict.Block(RemoteCode, "Invoke-Item z plikiem ze zmiennej — podaj plik wprost.")
                    : CheckProgram(target.Text, "", scope, fromScript: true);
            }

            if (lower == "ollama" || lower == "ollama.exe" || lower.EndsWith("\\ollama.exe", StringComparison.Ordinal))
            {
                return CheckOllama(invocation.Arguments.FirstOrDefault()?.Text);
            }

            if (!invocation.ViaCallOperator && !LooksLikeProgramName(name, scope))
            {
                return SafetyVerdict.Allowed;
            }

            return CheckProgram(name, string.Join(" ", invocation.Arguments.Select(a => a.Text)), scope, fromScript: true);
        }

        /// <summary>Polecenie ollama: wolno tylko odczytać (list, ps, show, wersja, pomoc) — modele i serwer obsługuje użytkownik.</summary>
        private static SafetyVerdict CheckOllama(string subcommand)
        {
            var sub = (subcommand ?? "").Trim().ToLowerInvariant();
            return sub.Length == 0 || sub == "list" || sub == "ls" || sub == "ps" || sub == "show" || sub == "-v" || sub == "--version"
                   || sub == "version" || sub == "help" || sub == "-h" || sub == "--help"
                ? SafetyVerdict.Allowed
                : SafetyVerdict.Block(Self, "ollama " + sub + " — modele i serwer Ollamy obsługuje użytkownik (instalator, ollama w PowerShellu).");
        }

        private static PowerShellPolicy.ScriptArgument ProgramArgument(PowerShellPolicy.ScriptInvocation invocation)
        {
            var arguments = invocation.Arguments;
            for (var i = 0; i < arguments.Count; i++)
            {
                var text = arguments[i].Text;
                if (Regex.IsMatch(text, "^-(f|fi|fil|file|filep|filepa|filepat|filepath|path)$", Options))
                {
                    return i + 1 < arguments.Count ? arguments[i + 1] : null;
                }
            }

            for (var i = 0; i < arguments.Count; i++)
            {
                if (arguments[i].Text.StartsWith("-", StringComparison.Ordinal))
                {
                    i++; // wartość parametru
                    continue;
                }

                return arguments[i];
            }

            return null;
        }

        // -------------------------------------------------------------------------------------------------- programy

        /// <summary>Ocena programu do uruchomienia (narzędzie <c>uruchom</c>, Start-Process, &amp;): nazwa, rozszerzenie, treść skryptu, argumenty.</summary>
        internal static SafetyVerdict CheckProgram(string program, string arguments, SafetyScope scope, bool fromScript = false)
        {
            if (string.IsNullOrWhiteSpace(program))
            {
                return SafetyVerdict.Allowed;
            }

            var raw = program.Trim().Trim('"', '\'');
            var combined = raw + " " + (arguments ?? "");
            var asked = SafetyVerdict.Allowed;
            if (!fromScript)
            {
                if (combined.Length > MaxScriptChars)
                {
                    return SafetyVerdict.Block(RemoteCode, "za długie argumenty programu, żeby je sprawdzić.");
                }

                var fromRules = Guarded(() => CheckProgramText(raw, arguments ?? "", combined, scope));
                if (fromRules.Kind == SafetyVerdictKind.Block)
                {
                    return fromRules;
                }

                asked = fromRules;
            }

            return SafetyVerdict.Stricter(CheckProgramName(raw, arguments, scope), asked);
        }

        /// <summary>
        /// Tekst uruchamianego programu z argumentami (narzędzie <c>uruchom</c>): wszystkie blokady i „zawsze pytaj” jak dla skryptu, który
        /// coś zmienia (<c>uruchom reg "add …\Run"</c> to to samo co w PowerShellu), adresy tylko z rozmowy.
        /// </summary>
        private static SafetyVerdict CheckProgramText(string raw, string arguments, string combined, SafetyScope scope)
        {
            var text = Normalize(combined, lowerCase: true);
            var cased = Normalize(combined, lowerCase: false);
            var bare = Path.GetFileNameWithoutExtension(Path.GetFileName(raw.Replace('/', '\\'))).ToLowerInvariant();
            var remoteLaunch = RemoteTools.Contains(bare) && !RemoteInstallArgs.IsMatch(arguments);
            foreach (var rule in BlockRules)
            {
                if (rule.PowerShellOnly || remoteLaunch && rule.Category == Exposure && rule.OnlyWhenChanging)
                {
                    continue; // samo otwarcie TeamViewera czy mstsc — zawsze pytaj (niżej), nie blokada
                }

                if (rule.Pattern.IsMatch(rule.CaseSensitive ? cased : text))
                {
                    return SafetyVerdict.Block(rule.Category, rule.Reason + ".");
                }
            }

            if (!scope.Internet && NeedsInternet.IsMatch(text))
            {
                return SafetyVerdict.Block(Leak, "dostęp do internetu jest wyłączony — instalacje i git sięgają do sieci.");
            }

            foreach (Match match in AnyUrl.Matches(combined))
            {
                var url = match.Value.TrimEnd('.', ',', ';', ':');
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !IsLoopback(uri.Host) && (!scope.Internet || !scope.IsKnownUrl(url)))
                {
                    return SafetyVerdict.Block(Leak, "otwieranie adresu " + Text.Shorten(url, 80) + ", którego nie podał użytkownik"
                        + (scope.Internet ? "" : " (internet wyłączony)") + ".");
                }
            }

            foreach (var rule in AskRules)
            {
                if (rule.Pattern.IsMatch(text))
                {
                    return SafetyVerdict.Ask(rule.Category, rule.Reason + ".");
                }
            }

            return RegistryPath.IsMatch(text) || text.IndexOf("hkcu\\", StringComparison.Ordinal) >= 0 || text.IndexOf("hklm\\", StringComparison.Ordinal) >= 0
                ? SafetyVerdict.Ask(Risky, "program dostaje ścieżkę rejestru Windows.")
                : SafetyVerdict.Allowed;
        }

        /// <summary>Program po nazwie i rozszerzeniu: sam Amadeusz, interpretery, narzędzia systemowe, skrypty, instalatory, programy spoza Program Files.</summary>
        private static SafetyVerdict CheckProgramName(string raw, string arguments, SafetyScope scope)
        {
            var name = Path.GetFileName(raw.Replace('/', '\\'));
            var extension = Path.GetExtension(name);
            var bare = (ExecutableExtensions.Contains(extension) ? Path.GetFileNameWithoutExtension(name) : name).ToLowerInvariant();
            var full = raw.IndexOfAny(new[] { '\\', '/', ':' }) >= 0 ? scope.Resolve(raw) : null;
            if (full == null && scope.WorkingFolder != null && extension.Length > 0)
            {
                var local = scope.Resolve(raw);
                if (local != null && File.Exists(local))
                {
                    full = local; // plik w folderze bieżącym podany samą nazwą
                }
            }

            if (scope.SelfPrograms.Contains(bare) || full != null && IsInside(full, scope.Protected) && (ExecutableExtensions.Contains(extension) || ScriptExtensions.Contains(extension)))
            {
                return SafetyVerdict.Block(Self, "uruchamianie samego Amadeusza albo programu z jego folderu — Amadeusz nie uruchamia sam siebie.");
            }

            if (bare == "ollama")
            {
                return CheckOllama((arguments ?? "").Trim().Split(' ').FirstOrDefault());
            }

            if (Shells.Contains(bare) || SystemTools.Contains(bare))
            {
                return SafetyVerdict.Block(RemoteCode, "uruchamianie " + bare + " — interpreter albo narzędzie systemowe; polecenia PowerShella pisz wprost narzędziem powershell.");
            }

            if (TransferTools.Contains(bare))
            {
                return SafetyVerdict.Block(Leak, "program do przesyłania plików poza komputer (" + bare + ") — tego asystent nie używa.");
            }

            if (RemoteTools.Contains(bare) && RemoteInstallArgs.IsMatch(arguments ?? ""))
            {
                return SafetyVerdict.Block(Exposure, "instalacja albo stały dostęp programu zdalnego dostępu (" + bare + ") — robi to tylko użytkownik.");
            }

            if (BlockedExtensions.Contains(extension))
            {
                return SafetyVerdict.Block(RemoteCode, "pliki " + extension + " Windows wykonuje jako kod — tego asystent nie uruchamia.");
            }

            if (Interpreters.Contains(bare))
            {
                var script = (arguments ?? "").Trim().Split(' ').FirstOrDefault(a => a.Length > 0 && !a.StartsWith("-", StringComparison.Ordinal));
                if (Regex.IsMatch(" " + (arguments ?? "") + " ", @"\s(-c|-e|--eval|-r|-p|--print)\s", Options))
                {
                    return SafetyVerdict.Block(RemoteCode, bare + " z kodem w linii — zapisz skrypt plikiem.");
                }

                return script == null
                    ? SafetyVerdict.Ask(Risky, "uruchamianie interpretera " + bare + ".")
                    : CheckScriptFile(scope.Resolve(script.Trim('"', '\'')), scope);
            }

            if (ScriptExtensions.Contains(extension))
            {
                return CheckScriptFile(full ?? scope.Resolve(raw), scope);
            }

            if (RemoteTools.Contains(bare))
            {
                return SafetyVerdict.Ask(Risky, "program zdalnego dostępu albo połączenia z innym komputerem (" + bare + ").");
            }

            if (InstallerExtensions.Contains(extension) || Regex.IsMatch(bare, @"(setup|install|installer|instalator|instaluj)", Options))
            {
                return SafetyVerdict.Ask(Risky, "instalacja programu (" + name + ").");
            }

            if (ShortcutExtensions.Contains(extension))
            {
                return SafetyVerdict.Ask(Risky, "skrót (" + name + ") może uruchomić dowolny program.");
            }

            if (string.Equals(extension, ".jar", StringComparison.OrdinalIgnoreCase))
            {
                return SafetyVerdict.Ask(Risky, "program Java (" + name + ").");
            }

            if (ExecutableExtensions.Contains(extension) && full != null && !IsInside(full, scope.ProgramFolders))
            {
                return SafetyVerdict.Ask(Risky, "program spoza folderów zainstalowanych programów (" + full + ").");
            }

            if (full == null && raw.IndexOfAny(new[] { '\\', '/' }) < 0 && FindOnPath(name) is string onPath && !IsInside(onPath, scope.ProgramFolders))
            {
                return SafetyVerdict.Ask(Risky, "program spoza folderów zainstalowanych programów (" + onPath + ").");
            }

            return SafetyVerdict.Allowed;
        }

        /// <summary>
        /// Gdzie Windows znajdzie program podany samą nazwą (foldery z PATH, rozszerzenia z PATHEXT); null, gdy nie ma go w PATH
        /// (wtedy uruchamia go rejestr „App Paths”, np. excel — zainstalowany program).
        /// </summary>
        private static string FindOnPath(string name)
        {
            try
            {
                var extensions = Path.HasExtension(name) ? new[] { "" } : new[] { ".exe", ".com", ".bat", ".cmd" };
                foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';').Where(f => f.Trim().Length > 0))
                {
                    foreach (var extension in extensions)
                    {
                        var candidate = Path.Combine(Environment.ExpandEnvironmentVariables(folder.Trim().Trim('"')), name + extension);
                        if (File.Exists(candidate))
                        {
                            return Path.GetFullPath(candidate);
                        }
                    }
                }
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException || e is IOException
                                      || e is UnauthorizedAccessException || e is System.Security.SecurityException)
            {
                // Dziwny wpis w PATH — traktujemy jak brak programu w PATH.
            }

            return null;
        }

        /// <summary>Skrypt z pliku: jego treść przechodzi te same zasady; uruchomienie zawsze z pytaniem.</summary>
        private static SafetyVerdict CheckScriptFile(string path, SafetyScope scope)
        {
            if (path == null)
            {
                return SafetyVerdict.Ask(Risky, "uruchamianie skryptu z pliku.");
            }

            if (IsInside(path, scope.Protected))
            {
                return SafetyVerdict.Block(Self, "uruchamianie skryptu z folderu chronionego (" + path + ").");
            }

            try
            {
                var info = new FileInfo(path);
                if (info.Exists && info.Length <= MaxScriptFileBytes)
                {
                    // Plik skryptu może być dłuższy niż polecenie od modelu — sprawdzamy go cały, z tym samym limitem czasu wzorców.
                    var content = File.ReadAllText(path);
                    var language = LanguageOf(Path.GetExtension(path));
                    var verdict = Guarded(() => CheckScriptCore(content, scope, language));
                    if (verdict.Kind == SafetyVerdictKind.Block)
                    {
                        return SafetyVerdict.Block(verdict.Category, "skrypt " + Path.GetFileName(path) + ": " + verdict.Reason);
                    }
                }
                else if (info.Exists)
                {
                    return SafetyVerdict.Block(RemoteCode, "skrypt " + Path.GetFileName(path) + " jest za duży, żeby go sprawdzić (" + info.Length / 1024
                        + " kB, najwyżej " + MaxScriptFileBytes / 1024 + " kB) — uruchom go sam.");
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
            {
                return SafetyVerdict.Ask(Risky, "uruchamianie skryptu, którego treści nie udało się sprawdzić (" + e.Message + ").");
            }

            return SafetyVerdict.Ask(Risky, "uruchamianie skryptu z pliku (" + Path.GetFileName(path) + ") — sprawdź jego treść.");
        }

        internal static ScriptLanguage LanguageOf(string extension)
        {
            switch ((extension ?? "").ToLowerInvariant())
            {
                case ".ps1":
                case ".psm1":
                case ".psd1":
                    return ScriptLanguage.PowerShell;
                case ".bat":
                case ".cmd":
                    return ScriptLanguage.Batch;
                case ".py":
                case ".pyw":
                    return ScriptLanguage.Python;
                default:
                    return ScriptLanguage.Other;
            }
        }

        private static SafetyVerdict CheckPython(string text)
        {
            if (Regex.IsMatch(text, @"\bexec\s*\(|\beval\s*\(|__import__\s*\(|\bbase64\.b64decode\b|\bmarshal\.loads\b|\bpickle\.loads\b|\bctypes\b|\bsocket\.socket\b|\bsocketserver\b|\bhttp\.server\b|\brequests\.(post|put|patch)\b|\burlopen\([^)]*data\s*=|\bftplib\b|\bsmtplib\b|\bparamiko\b|\bwinreg\.(setvalue|setvalueex|createkey|deletekey|deletevalue)\b", Options))
            {
                return SafetyVerdict.Block(RemoteCode, "skrypt Pythona wykonuje kod w locie, otwiera sieć albo zmienia rejestr.");
            }

            return Regex.IsMatch(text, @"\bsubprocess\b|\bos\.(system|popen|startfile|exec\w*|spawn\w*)\b|\bshutil\.rmtree\b|\bos\.remove\b", Options)
                ? SafetyVerdict.Ask(Risky, "skrypt Pythona uruchamia programy albo kasuje pliki.")
                : SafetyVerdict.Allowed;
        }

        // -------------------------------------------------------------------------------------------------- pliki i ścieżki

        /// <summary>Plik zapisywany narzędziem: miejsce, rodzaj i — dla skryptów — treść.</summary>
        internal static SafetyVerdict CheckWrittenFile(string path, string content, SafetyScope scope)
        {
            var full = scope.Resolve(path);
            var extension = Path.GetExtension(full ?? path ?? "");
            if (BlockedExtensions.Contains(extension) || ShortcutExtensions.Contains(extension))
            {
                return SafetyVerdict.Block(RemoteCode, "zapis pliku " + extension + " (Windows go wykonuje) — tego asystent nie robi.");
            }

            if (ScriptExtensions.Contains(extension))
            {
                var verdict = CheckScript(content, scope, LanguageOf(extension));
                if (verdict.Kind == SafetyVerdictKind.Block)
                {
                    return SafetyVerdict.Block(verdict.Category, "treść skryptu: " + verdict.Reason);
                }
            }

            if (string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(content))
            {
                foreach (var cell in content.Split(new[] { '\n', ';', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var formula = CheckFormula(cell.Trim().Trim('"'), csvCell: true);
                    if (formula.Kind == SafetyVerdictKind.Block)
                    {
                        return formula;
                    }
                }
            }

            return SafetyVerdict.Allowed;
        }

        /// <summary>
        /// Ścieżka z argumentu narzędzia: sekret — blokada (także czytanie i kopiowanie); zapis w folderze chronionym albo
        /// w podfolderze <c>.amadeusz</c> — blokada; źródło, które jest folderem chronionym albo zawiera sekrety — blokada.
        /// </summary>
        internal static SafetyVerdict CheckPath(string fullPath, bool write, SafetyScope scope, bool isSource = false)
        {
            if (AdminShare.IsMatch(fullPath))
            {
                return SafetyVerdict.Block(Office, "udział administracyjny innego komputera (" + fullPath + ") — tego asystent nie używa.");
            }

            if (IsSecret(fullPath, scope))
            {
                return SafetyVerdict.Block(Secrets, "plik albo folder z hasłami lub kluczami (" + fullPath + ") — tego asystent nie czyta, nie kopiuje i nie wysyła.");
            }

            if (isSource && scope.Secrets.Any(s => IsInside(s, new[] { Slash(fullPath) })))
            {
                return SafetyVerdict.Block(Secrets, "folder " + fullPath + " zawiera hasła albo klucze — tego asystent nie kopiuje ani nie wysyła.");
            }

            if (isSource && write && scope.Protected.Any(p => IsInside(p, new[] { Slash(fullPath) })))
            {
                return SafetyVerdict.Block(Self, "folder " + fullPath + " zawiera pliki samego Amadeusza — tego asystent nie przenosi ani nie wysyła.");
            }

            if (!write)
            {
                return SafetyVerdict.Allowed;
            }

            if (IsInside(fullPath, scope.Protected) || HasAmadeuszSegment(fullPath) || ProtectedName.IsMatch(fullPath))
            {
                return SafetyVerdict.Block(Self, "zmiana plików samego Amadeusza (" + fullPath + "): program, umiejętności, ustawienia albo rozmowy. Czytać wolno; zmienia tylko użytkownik.");
            }

            if (IsInside(fullPath, scope.SystemProtected) || AutostartPath.IsMatch(fullPath))
            {
                return SafetyVerdict.Block(Autostart, "zapis w folderze systemu albo autostartu (" + fullPath + ") — tego asystent nie robi.");
            }

            var extension = Path.GetExtension(fullPath);
            if (BlockedExtensions.Contains(extension) || ShortcutExtensions.Contains(extension))
            {
                return SafetyVerdict.Block(RemoteCode, "tworzenie pliku " + extension + " (Windows go wykonuje) — tego asystent nie robi.");
            }

            return fullPath.StartsWith("\\\\", StringComparison.Ordinal)
                ? SafetyVerdict.Ask(Risky, "zapis na dysku sieciowym (" + fullPath + ").")
                : SafetyVerdict.Allowed;
        }

        /// <summary>Rozszerzenie pliku, który Windows wykonuje, a którego asystent nie tworzy (.reg, .vbs, .lnk…).</summary>
        internal static bool IsBlockedFileExtension(string extension)
            => BlockedExtensions.Contains(extension ?? "") || ShortcutExtensions.Contains(extension ?? "");

        internal static bool IsSecret(string fullPath, SafetyScope scope)
        {
            if (string.IsNullOrEmpty(fullPath))
            {
                return false;
            }

            var name = Path.GetFileName(fullPath.TrimEnd('\\', '/'));
            if (SecretFileNames.Contains(name) || SecretFilePattern.IsMatch(name))
            {
                return true;
            }

            return scope.Secrets.Any(secret => string.Equals(fullPath.TrimEnd('\\'), secret.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                                               || IsInside(fullPath, new[] { Slash(secret) }));
        }

        private static bool HasAmadeuszSegment(string fullPath)
            => fullPath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).Any(s => s.Equals(".amadeusz", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Dostawcy w chmurze, którym wolno dostać dane samego Amadeusza (ustawienia, dzienniki, rozmowy). Decyzja użytkownika
        /// z 2026-11-19: Claude tak (jak przy bazie CDE), Gemini i GPT — nie.
        /// </summary>
        internal static readonly HashSet<string> TrustedCloudsForPrivateData = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "anthropic" };

        /// <summary>Czy model tego dostawcy (null = lokalny) nie może dostać danych samego Amadeusza.</summary>
        internal static bool IsUntrustedCloud(string cloud) => !string.IsNullOrWhiteSpace(cloud) && !TrustedCloudsForPrivateData.Contains(cloud.Trim());

        /// <summary>Plik albo folder z prywatnymi danymi Amadeusza: ustawienia, dzienniki, rozmowy (także każdy folder <c>.amadeusz</c>).</summary>
        internal static bool IsPrivate(string fullPath, SafetyScope scope)
            => !string.IsNullOrEmpty(fullPath) && (IsInside(fullPath, scope.Private) || HasAmadeuszSegment(fullPath));

        internal static SafetyVerdict PrivateDataBlock(string path)
            => SafetyVerdict.Block(Leak, "dane samego Amadeusza (" + Text.Shorten(path, 90) + " — ustawienia, dzienniki, rozmowy) nie idą do modeli Gemini ani GPT, "
                + "a ten model liczy w ich chmurze. Przełącz na model lokalny albo Claude.");

        /// <summary>
        /// Model Gemini albo GPT: argumenty skryptu w folderach z prywatnymi danymi Amadeusza — blokada; odczyt treści z gwiazdką albo
        /// rekurencyjnie z folderu, w którym takie dane leżą — blokada (lista nazw plików — nie).
        /// </summary>
        private static SafetyVerdict CheckPrivateArguments(PowerShellPolicy.ScriptFacts facts, SafetyScope scope)
        {
            if (!scope.UntrustedCloud)
            {
                return null;
            }

            var working = scope.WorkingFolder == null ? null : scope.Resolve(scope.WorkingFolder);
            if (working != null && IsPrivate(working, scope))
            {
                return PrivateDataBlock(working);
            }

            foreach (var invocation in facts.Invocations)
            {
                var content = ContentCommands.Contains(invocation.Name);
                var recurse = content && (invocation.Arguments.Any(a => IsRecurseSwitch(a.Text)) || RecursiveTools.Contains(invocation.Name));
                foreach (var argument in invocation.Arguments.Where(a => !a.Dynamic && !a.Text.StartsWith("-", StringComparison.Ordinal) || a.Text.IndexOf(':') > 1))
                {
                    var full = scope.Resolve(argument.Text);
                    if (full == null)
                    {
                        continue;
                    }

                    if (IsPrivate(full, scope))
                    {
                        return PrivateDataBlock(full);
                    }

                    if (content && (recurse || argument.Text.IndexOfAny(new[] { '*', '?' }) >= 0) && scope.Private.Any(p => IsInside(p, new[] { Slash(full) })))
                    {
                        return PrivateDataBlock(full + " (w środku są dane Amadeusza — wskaż węższy folder)");
                    }
                }
            }

            return null;
        }

        internal static bool IsInside(string fullPath, IEnumerable<string> folders)
        {
            var path = Slash(fullPath);
            return folders.Any(folder => path.StartsWith(Slash(folder), StringComparison.OrdinalIgnoreCase));
        }

        private static string Slash(string path) => (path ?? "").Replace('/', '\\').TrimEnd('\\') + "\\";

        // -------------------------------------------------------------------------------------------------- adresy i formuły

        /// <summary>Adres strony do przeczytania: tylko z rozmowy (dokładnie albo strona bez parametrów w domenie podanej przez użytkownika).</summary>
        internal static SafetyVerdict CheckFetchUrl(string address, Func<string, bool> isKnown)
        {
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
            {
                return SafetyVerdict.Allowed; // WebAccess sam zwróci czytelny błąd
            }

            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                return SafetyVerdict.Block(Leak, "adres z nazwą użytkownika albo hasłem — tego asystent nie otwiera.");
            }

            return isKnown(address)
                ? SafetyVerdict.Allowed
                : SafetyVerdict.Block(Leak, "adresu " + Text.Shorten(address, 80) + " nie ma w rozmowie ani w wynikach wyszukiwania. Najpierw wyszukaj stronę ("
                    + BuiltInTools.WebSearchName + ") albo poproś użytkownika o adres — asystent nie otwiera adresów, które sam złożył (mogłyby wynieść dane).");
        }

        /// <summary>
        /// Formuła Excela sięgająca sieci albo programów: WEBSERVICE, FILTERXML, IMAGE, RTD, CALL, DDE. Formuła to jeden wiersz zaczynający
        /// się od „=” (tak wpisuje ją umiejętność office); w komórce CSV Excel uznaje za formułę także „+”, „-” i „@”.
        /// Punktor „- Obraz (rys. 3)” w tekście slajdu formułą nie jest.
        /// </summary>
        internal static SafetyVerdict CheckFormula(string value, bool csvCell = false)
        {
            if (string.IsNullOrEmpty(value))
            {
                return SafetyVerdict.Allowed;
            }

            var text = value.TrimStart();
            if (text.Length < 2 || text.IndexOf('\n') >= 0 || (csvCell ? "=+-@" : "=").IndexOf(text[0]) < 0)
            {
                return SafetyVerdict.Allowed;
            }

            return Guarded(() => DangerousFormula.IsMatch(text)
                ? SafetyVerdict.Block(Leak, "formuła sięga do internetu albo uruchamia programy (WEBSERVICE, IMAGE, RTD, DDE…) — tego asystent nie wpisuje.")
                : SafetyVerdict.Allowed);
        }

        internal static string NormalizeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                return null;
            }

            var host = uri.Host.ToLowerInvariant();
            var port = uri.IsDefaultPort ? "" : ":" + uri.Port;
            var path = uri.AbsolutePath.TrimEnd('/');
            return uri.Scheme + "://" + host + port + path + uri.Query;
        }

        /// <summary>Czy któryś człon ścieżki adresu wygląda na dane (base64, ciąg szesnastkowy) — nazwy stron z myślnikami nie.</summary>
        internal static bool HasDataLikeSegment(string path)
            => (path ?? "").Split('/').Any(segment => segment.Length > 0 && DataLikeSegment.IsMatch(segment));

        /// <summary>Domena „do zapamiętania” (bez www., przy krajowych końcówkach co.uk, com.pl — trzy człony).</summary>
        internal static string RegistrableDomain(string host)
        {
            var labels = (host ?? "").Trim('.').ToLowerInvariant().Split('.');
            if (labels.Length <= 2)
            {
                return string.Join(".", labels);
            }

            var second = labels[labels.Length - 2];
            var take = labels[labels.Length - 1].Length == 2 && (second == "co" || second == "com" || second == "org" || second == "net" || second == "gov"
                                                                || second == "edu" || second == "ac" || second == "waw" || second == "info")
                ? 3
                : 2;
            return string.Join(".", labels.Skip(labels.Length - take));
        }

        /// <summary>Nazwy plików (rysunek.pdf, plan.dwg) wyglądają jak domeny — nie są adresami stron.</summary>
        internal static bool LooksLikeFileName(string text)
            => Regex.IsMatch(text ?? "", @"\.(pdf|dwg|dxf|docx?|xlsx?|pptx?|txt|csv|json|xml|md|png|jpe?g|zip|7z|rar|exe|msi|ps1|bat|cmd|py|js|ifc|rf5|rf6|tcz|db1|log|ini|cs|dll)$", Options);

        private static bool IsLoopback(string host)
        {
            var h = (host ?? "").Trim('[', ']').ToLowerInvariant();
            return h == "localhost" || h == "::1" || h == "0.0.0.0" || h.StartsWith("127.", StringComparison.Ordinal)
                   || h == Environment.MachineName.ToLowerInvariant();
        }

        private static bool IsLocalNetworkHost(string host)
        {
            var h = (host ?? "").Trim('[', ']').ToLowerInvariant();
            if (IPAddress.TryParse(h, out var address))
            {
                return WebAccess.IsPrivate(address);
            }

            return h.IndexOf('.') < 0 || h.EndsWith(".local", StringComparison.Ordinal) || h.EndsWith(".lan", StringComparison.Ordinal)
                   || h.EndsWith(".internal", StringComparison.Ordinal) || h.EndsWith(".corp", StringComparison.Ordinal)
                   || h.EndsWith(".home", StringComparison.Ordinal) || h.EndsWith(".intranet", StringComparison.Ordinal);
        }

        // -------------------------------------------------------------------------------------------------- normalizacja

        /// <summary>
        /// Skrypt bez komentarzy (nic nie wykonują): PowerShell <c># …</c> i <c>&lt;# … #&gt;</c>, Python <c># …</c> — poza tekstem
        /// w cudzysłowie; cmd: wiersze <c>rem</c> i <c>::</c>. Nowe linie zostają.
        /// </summary>
        internal static string StripComments(string script, ScriptLanguage language)
        {
            if (string.IsNullOrEmpty(script))
            {
                return script ?? "";
            }

            if (language == ScriptLanguage.Batch)
            {
                return string.Join("\n", script.Replace("\r", "").Split('\n')
                    .Select(line => Regex.IsMatch(line, @"^\s*@?(rem(\s|$)|::)", RegexOptions.IgnoreCase) ? "" : line));
            }

            if (language != ScriptLanguage.PowerShell && language != ScriptLanguage.Python)
            {
                return script;
            }

            var builder = new StringBuilder(script.Length);
            char quote = '\0';
            for (var i = 0; i < script.Length; i++)
            {
                var c = script[i];
                if (quote != '\0')
                {
                    builder.Append(c);
                    if (c == '`' && language == ScriptLanguage.PowerShell && i + 1 < script.Length)
                    {
                        builder.Append(script[++i]); // znak ucieczki w PowerShellu
                    }
                    else if (c == '\\' && language == ScriptLanguage.Python && i + 1 < script.Length)
                    {
                        builder.Append(script[++i]);
                    }
                    else if (c == quote || c == '\n' && language == ScriptLanguage.Python)
                    {
                        quote = '\0';
                    }

                    continue;
                }

                if (c == '\'' || c == '"')
                {
                    quote = c;
                    builder.Append(c);
                    continue;
                }

                if (language == ScriptLanguage.PowerShell && c == '<' && i + 1 < script.Length && script[i + 1] == '#')
                {
                    var end = script.IndexOf("#>", i + 2, StringComparison.Ordinal);
                    var skipped = end < 0 ? script.Substring(i) : script.Substring(i, end + 2 - i);
                    builder.Append('\n', skipped.Count(ch => ch == '\n'));
                    i = end < 0 ? script.Length : end + 1;
                    continue;
                }

                if (c == '#' && (i == 0 || char.IsWhiteSpace(script[i - 1]) || script[i - 1] == ';' || script[i - 1] == '{' || script[i - 1] == '}'))
                {
                    while (i < script.Length && script[i] != '\n')
                    {
                        i++;
                    }

                    if (i < script.Length)
                    {
                        builder.Append('\n');
                    }

                    continue;
                }

                builder.Append(c);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Tekst do porównań: myślniki typograficzne → „-”, cudzysłowy typograficzne → zwykłe, bez znaków ucieczki (` w PowerShellu,
        /// ^ w cmd), sklejone sąsiednie napisy ('a'+'b' → 'ab'), białe znaki ściśnięte (nowe linie zostają).
        /// </summary>
        internal static string Normalize(string script, bool lowerCase)
        {
            var builder = new StringBuilder(script.Length);
            foreach (var c in script)
            {
                switch (c)
                {
                    case '\u2010':
                    case '\u2011':
                    case '\u2012':
                    case '\u2013':
                    case '\u2014':
                    case '\u2015':
                    case '\u2212':
                        builder.Append('-');
                        break;
                    case '\u2018':
                    case '\u2019':
                    case '\u201A':
                    case '\u201B':
                        builder.Append('\'');
                        break;
                    case '\u201C':
                    case '\u201D':
                    case '\u201E':
                    case '\u201F':
                        builder.Append('"');
                        break;
                    case '`':
                    case '^':
                        break;
                    case '\r':
                        break;
                    case '\t':
                    case '\u00A0':
                        builder.Append(' ');
                        break;
                    default:
                        builder.Append(c);
                        break;
                }
            }

            var text = Concatenation.Replace(builder.ToString(), "");
            text = Regex.Replace(text, @"[ ]{2,}", " ");
            return lowerCase ? text.ToLowerInvariant() : text;
        }
    }
}
