// Creează un AWB, descarcă eticheta A6 și afișează costul.
// Rulare: DRAGONSTAR_API_KEY=... node examples/create-awb.js
'use strict';

const fs = require('node:fs');
const { DragonStarClient, DragonStarError } = require('../src');

async function main() {
  const dsc = new DragonStarClient(process.env.DRAGONSTAR_API_KEY);

  try {
    const created = await dsc.createAwb({
      shipmentType: 'parcel',
      weight: 2.5,
      pieces: 1,
      recipientName: 'Ion Popescu',
      recipientAddress: 'Str. Memorandumului 1, ap. 3',
      recipientCounty: 'Cluj',
      recipientCity: 'Cluj-Napoca',
      recipientPhone: '0722123456',
      codAmount: 149.9,
      codPaymentType: 'cash',
      smsNotification: true,
      notes: 'Comanda #1001',
    });
    console.log(`AWB ${created.awb} — total ${created.totalAmount} RON`);

    const pdf = await dsc.getLabel(created.awb, 'A6');
    fs.writeFileSync(`awb-${created.awb}.pdf`, pdf);
    console.log(`Eticheta salvată în awb-${created.awb}.pdf`);
  } catch (err) {
    if (err instanceof DragonStarError && err.code === 'validation_failed') {
      console.error('Date invalide:', err.errors);
    } else {
      console.error(err.message);
    }
    process.exitCode = 1;
  }
}

main();
