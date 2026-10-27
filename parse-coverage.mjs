import fs from 'node:fs';

const xml = fs.readFileSync('coverage/coverage.cobertura.xml', 'utf8');
const regex = /<class name="([^"]+)" filename="([^"]+)" line-rate="([^"]+)" branch-rate="([^"]+)"/g;
let m;
const classes = [];
while ((m = regex.exec(xml)) !== null) {
  classes.push({
    name: m[1],
    file: m[2].split('\\').pop() ?? m[2],
    lineRate: Number((parseFloat(m[3]) * 100).toFixed(1)),
    branchRate: Number((parseFloat(m[4]) * 100).toFixed(1)),
  });
}
classes.sort((a, b) => a.branchRate - b.branchRate);
console.log('Total classes:', classes.length);
console.table(classes.filter(c => c.branchRate < 80).slice(0, 30));
