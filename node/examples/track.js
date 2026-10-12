// Urmărește unul sau mai multe AWB-uri.
// Rulare: DRAGONSTAR_API_KEY=... node examples/track.js 94348148 94348149
'use strict';

const { DragonStarClient, statusLabel, isFinal } = require('../src');

async function main() {
  const dsc = new DragonStarClient(process.env.DRAGONSTAR_API_KEY);
  const awbs = process.argv.slice(2).map(Number);
  if (awbs.length === 0) {
    console.error('Folosire: node examples/track.js <awb> [awb...]');
    process.exitCode = 1;
    return;
  }

  if (awbs.length === 1) {
    const t = await dsc.track(awbs[0]);
    console.log(`${awbs[0]}: ${t.status.descriptionRo}`);
    for (const e of t.events || []) {
      console.log(`  ${e.timestamp}  ${e.location.name.padEnd(12)} ${e.description}`);
    }
    return;
  }

  const res = await dsc.trackMany(awbs);
  for (const item of res.data) {
    const code = item.status.statusCode;
    console.log(`${item.awb}: ${statusLabel(code)}${isFinal(code) ? ' (final)' : ''}`);
  }
  if (res.notFound.length) console.log('Negăsite:', res.notFound.join(', '));
}

main().catch((err) => {
  console.error(err.message);
  process.exitCode = 1;
});
