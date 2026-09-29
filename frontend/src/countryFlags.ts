// Ländernamen (deutsch/englisch, wie sie per Hand oder Google-Import ankommen)
// auf ISO-3166-Alpha-2-Codes abbilden, um daraus ein Flaggen-Emoji zu machen.
const NAME_TO_ISO: Record<string, string> = {
  deutschland: 'DE', germany: 'DE',
  österreich: 'AT', austria: 'AT',
  schweiz: 'CH', switzerland: 'CH',
  frankreich: 'FR', france: 'FR',
  italien: 'IT', italy: 'IT', italia: 'IT',
  spanien: 'ES', spain: 'ES', españa: 'ES',
  portugal: 'PT',
  niederlande: 'NL', netherlands: 'NL', holland: 'NL', nederland: 'NL',
  belgien: 'BE', belgium: 'BE',
  luxemburg: 'LU', luxembourg: 'LU',
  dänemark: 'DK', denmark: 'DK', danmark: 'DK',
  norwegen: 'NO', norway: 'NO', norge: 'NO',
  schweden: 'SE', sweden: 'SE', sverige: 'SE',
  finnland: 'FI', finland: 'FI',
  island: 'IS', iceland: 'IS',
  irland: 'IE', ireland: 'IE',
  großbritannien: 'GB', 'vereinigtes königreich': 'GB', 'united kingdom': 'GB',
  'great britain': 'GB', england: 'GB', schottland: 'GB', scotland: 'GB', wales: 'GB',
  polen: 'PL', poland: 'PL', polska: 'PL',
  tschechien: 'CZ', czechia: 'CZ', 'czech republic': 'CZ',
  slowakei: 'SK', slovakia: 'SK',
  ungarn: 'HU', hungary: 'HU',
  slowenien: 'SI', slovenia: 'SI',
  kroatien: 'HR', croatia: 'HR', hrvatska: 'HR',
  'bosnien und herzegowina': 'BA', bosnia: 'BA', 'bosnia and herzegovina': 'BA',
  serbien: 'RS', serbia: 'RS',
  montenegro: 'ME',
  albanien: 'AL', albania: 'AL',
  nordmazedonien: 'MK', 'north macedonia': 'MK',
  griechenland: 'GR', greece: 'GR',
  bulgarien: 'BG', bulgaria: 'BG',
  rumänien: 'RO', romania: 'RO',
  türkei: 'TR', turkey: 'TR', türkiye: 'TR',
  estland: 'EE', estonia: 'EE',
  lettland: 'LV', latvia: 'LV',
  litauen: 'LT', lithuania: 'LT',
  marokko: 'MA', morocco: 'MA',
  usa: 'US', 'vereinigte staaten': 'US', 'united states': 'US',
  kanada: 'CA', canada: 'CA',
}

// Regionalindikator-Emojis: 🇦 beginnt bei U+1F1E6, also Offset zu 'A' (65).
const REGIONAL_INDICATOR_OFFSET = 0x1f1e6 - 65

export function countryFlag(country: string | null | undefined): string | null {
  if (!country) return null
  const key = country.trim().toLowerCase()
  const iso = NAME_TO_ISO[key] ?? (/^[a-z]{2}$/.test(key) ? key.toUpperCase() : null)
  if (!iso) return null
  return String.fromCodePoint(
    ...[...iso].map((c) => REGIONAL_INDICATOR_OFFSET + c.charCodeAt(0)),
  )
}
