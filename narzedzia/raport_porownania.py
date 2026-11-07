# -*- coding: utf-8 -*-
"""Tabela porównania modeli z wyników LokalneAI.Eval (pliki *.json w folderze wyników).

Użycie:  py -3.12 -X utf8 narzedzia\\raport_porownania.py wyniki\\porownanie2 [plik.md]
Do tabeli trafiają tylko liczby trafień i czasy — bez treści odpowiedzi (te mają godziny ludzi).
"""
import glob
import json
import os
import sys
from collections import defaultdict


def main() -> None:
    folder = sys.argv[1] if len(sys.argv) > 1 else os.path.join("wyniki", "porownanie2")
    out = sys.argv[2] if len(sys.argv) > 2 else None
    runs = defaultdict(dict)
    failures = defaultdict(list)
    for path in sorted(glob.glob(os.path.join(folder, "*.json"))):
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        s = data["podsumowanie"]
        runs[s["model"]][s["profil"]] = s  # ostatni przebieg wygrywa
        failures[(s["model"], s["profil"])] = [
            f'{w["test"]}/{w["krok"]}' for w in data["wyniki"] if not w["ok"]
        ]

    lines = [
        "| Model | Tekla: trafne | Tekla: s/krok | CDE: SQL zgodny | CDE: odpowiedź zgodna | CDE: s/krok | Kontekst max |",
        "|---|---|---|---|---|---|---|",
    ]
    for model, profiles in sorted(runs.items(), key=lambda kv: -score(kv[1])):
        t = profiles.get("tekla")
        c = profiles.get("cde")
        lines.append("| {} | {} | {} | {} | {} | {} | {} |".format(
            model,
            pct(t, "procent") if t else "—",
            num(t, "sredni_czas_s") if t else "—",
            pct(c, "procent") if c else "—",
            f'{round(100 * c["odpowiedzi_trafne"] / c["krokow"], 1)}%' if c and c.get("odpowiedzi_trafne") is not None and c["krokow"] else "—",
            num(c, "sredni_czas_s") if c else "—",
            max([p.get("max_kontekst", 0) for p in profiles.values()] or [0]),
        ))

    lines.append("")
    lines.append("Nietrafione kroki (test/krok):")
    for (model, profile), items in sorted(failures.items()):
        lines.append(f"- {model} / {profile}: {', '.join(items) if items else 'brak'}")

    text = "\n".join(lines)
    print(text)
    if out:
        with open(out, "w", encoding="utf-8") as f:
            f.write(text + "\n")


def pct(s, key):
    return f'{s[key]}% ({s["trafne"]}/{s["krokow"]})'


def num(s, key):
    return f'{s[key]:.1f}'.replace(".", ",")


def score(profiles):
    return sum(p.get("procent", 0) for p in profiles.values())


if __name__ == "__main__":
    main()
