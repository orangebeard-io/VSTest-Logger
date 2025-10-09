<h1 align="center">
  <a href="https://github.com/orangebeard-io/VSTest-Logger">
    <img src="https://raw.githubusercontent.com/orangebeard-io/VSTest-Logger/master/.github/logo.svg" alt="Orangebeard.io FitNesse TestSystemListener" height="200">
  </a>
  <br>Orangebeard.io VsTest Logger<br>
</h1>

<h4 align="center">A Report Logger to report VStest tests in Orangebeard.</h4>

<p align="center">
  <a href="https://github.com/orangebeard-io/VSTest-Logger/blob/master/LICENSE.txt">
    <img src="https://img.shields.io/github/license/orangebeard-io/VSTest-Logger?style=flat-square"
      alt="License" />
  </a>
</p>

<div align="center">
  <h4>
    <a href="https://orangebeard.io">Orangebeard</a> |
    <a href="#build">Build</a> |
    <a href="#install">Install</a>
  </h4>
</div>

## Build
 * Clone this repository
 * Open in a .Net IDE
 * Reference the Orangebeard.Client DLL (Find it on NuGet)
 * Build the Logger DLL

## Install & use

 * Get the logger from NuGet, or build it yourself
 * Reference the Logger in your Solution, make sure it is copied to your output directory
 * Configure the logger using json or environment variables
 * Run using vstest.console using /Logger Orangebeard, or via dotnet test -l Orangebeard
 * To run from inside VS, use a runsettings file:
```xml
    <?xml version="1.0" encoding="UTF-8"?>
    <RunSettings>
      <RunConfiguration>
        <TestAdaptersPaths>.</TestAdaptersPaths>
      </RunConfiguration>
      <LoggerRunSettings>
        <Loggers>
          <Logger friendlyName="Orangebeard">
            <Configuration>
              <TestSet.Description>Unit Test Run From VS</TestSet.Description>
            </Configuration>
          </Logger>
        </Loggers>
      </LoggerRunSettings>
    </RunSettings>
```

Configuration: orangebeard.json

```json
{
  "endpoint": "https://app.orangebeard.io/my-org",
  "accessToken": "listener-access-token",
  "project": "projectname",
  "testset": "My Test Suite",
  "description": "A description",
  "attributes": [
    {
      "key": "Tool",
      "value": "VS"
    }
  ],
  "reference_url": "https://docs.orangebeard.io/"
}


```

Now run your test as you normally do and see the results fly in to Orangebeard!

