# Quality Control — fælder, invarianter, tekst der lover for meget

## Projekt
EA-register "LeanIX-light" for ITFL/DTU. Brugere: EA (kurator/admin), travle systemforvaltere, teamledere, sikkerhed.
Plan 2026-09-24: 1 register, 2 integrationer+CSV, 3 kapabiliteter+overlap, 4 forvaltere+audit, 5 teknologi/EOL,
6 Excel-import, 7 SOP, 8 kontrakter.

## Plan-gennemgang 1 (2026-09-24) — fund at følge op på i koden
- Navneunikhed globalt vs. moduler: generiske modulnavne ("Rapportering") i to produkter kolliderer. Spørg: scope?
  Og viser listen modul-rækker med forældernavn?
- Livscyklus-værdier bliver EKSTERN kontrakt i delopg. 2 (CSV-skabelon til foranalysen). Enum-værdier/labels skal
  ligge fast før da. "Udfases"/"Udfaset" er ét bogstav fra hinanden — foreslået "Nedlagt".
- Default "I drift" + valgfrit team: listen skal kunne vise HULLERNE (filter "uden team", "ikke angivet").
- "Ansvarligt team" kolliderer med systemejer-rollen i delopg. 4. Spørg om label.
- Slet vs. Udfaset: slet = fejloprettet; knap skal være deaktiveret m. forklaring, ikke 409 efter klik.
- Forælder-vælger må kun tilbyde gyldige forældre (ellers lover UI'et mere end serveren tillader).
- Overlap (delopg. 3): forælder+modul og Indfases+Udfases-par er ikke konsolideringskandidater. Type "udtræk" skal
  ekskluderes.
- Match-nøgle til import (Id/ExternalKey) skal være i CSV-eksporten fra delopg. 2, ikke først i 6.

## Generelle spørgsmål til dette projekt
- Beregnes knap-synlighed (canEdit/canCreate/canDelete) af serveren, eller sammenligner klienten rollestrenge?
- 409 ved samtidig redigering: dansk besked, og mister brugeren sine indtastninger?
