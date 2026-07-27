const sqlFormatter = require('./wwwroot/js/sql-formatter.min.js');
const sql = `INSERT INTO dbo.Currencies (
    CurrencyCode, CurrencyName,
Symbol)
    VALUES ('USD', 'US Dollar', '$'),
    ('EUR', 'Euro', '€'), ('GBP', 'British Pound', '£');`;

try {
    const formatted = sqlFormatter.format(sql, { 
        language: 'tsql',
        keywordCase: 'upper',
        linesBetweenQueries: 2
    });
    console.log("tsql:", formatted);
} catch (e) {
    console.error("tsql Error:", e.message);
}
