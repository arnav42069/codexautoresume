'use strict';

require('./installer.cjs').createInstaller().install().then(() => {
  console.log('Installed. Run codexautoresume to open the app.');
}).catch(error => {
  console.error(error.message);
  process.exitCode = 1;
});
