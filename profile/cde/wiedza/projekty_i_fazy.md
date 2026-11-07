# Projekty i fazy

Projekt rozpoznaje się po numerze `code` (4 cyfry, tekst, np. '1095'). Symbol (`symbol`, np. WSP) jest krótkim znacznikiem i NIE jest unikalny: ABS, CWS, MRO, SZK, ZGS mają po dwa projekty. Przy symbolu pasującym do kilku projektów należy dopytać, o który chodzi. Nazwa (`name`) bywa nieprecyzyjna, szukaj przez ILIKE.

Przykłady: 1095 = WSP (Włocławek spalarnia, faza R), 1063 = CPK (Baranów, R), 0546 = PMZ (optymalizacja konstrukcji reaktora SCR Pomorzany, zakończony).

Fazy (`ref.project_phase`): O ofertowy, R realizacja, B w trakcie budowy, W wstrzymany, Z zakończony, X oferta bez realizacji, N niebiznesowy (pseudo-projekty wewnętrzne: urlop, oferty, organizacja). Zestaw "aktywne" to O, R, B, W (`phase_is_active`). Stan na 2026-11-06: ok. 722 projekty, w tym Z ok. 410, X ok. 272, N 14, B 13, R 9, W 2, O 2. Numer 9000 to projekt testowy (faza R), pomijaj go w statystykach.

"Projekt w realizacji" w mowie potocznej bywa rozumiany jako faza R albo jako wszystkie aktywne; przy liczeniu pokaż rozbicie na fazy. Kod wewnętrzny 0000 oraz wpisy bez projektu to zadania wewnętrzne biura (nie są projektami).
