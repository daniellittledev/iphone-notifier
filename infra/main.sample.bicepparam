using './main.bicep'

// Copy to main.bicepparam and fill in. Do NOT commit real secrets.
param namePrefix = 'ntfy'
param appleBundleId = 'com.example.iphonenotifier'
param apnsKeyId = 'XXXXXXXXXX'
param apnsTeamId = 'YYYYYYYYYY'
param apnsEnvironment = 'Sandbox'

// Provide these at deploy time instead of committing them, e.g.:
//   az deployment group create ... --parameters apnsKey=@AuthKey.p8 jwtSigningKey=$(openssl rand -base64 32)
param apnsKey = readEnvironmentVariable('APNS_KEY')
param jwtSigningKey = readEnvironmentVariable('JWT_SIGNING_KEY')
