targetScope = 'local'

extension myextension with {
    authenticationMode: 'ManagedIdentity'
    objectId: 'dddddddd-dddd-dddd-dddd-dddddddddddd' 
}

resource someResource 'MyResource' = {
    name: 'myResourceName'
    operation: 'Reverse'
}

resource someResourceExisting 'MyResource' existing = {
    name: 'existingResourceName'
}

output existingResourceNameOutput string = someResourceExisting.name
