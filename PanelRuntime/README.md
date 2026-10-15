# The PannelRuntime is what allows M-Vison that have the customization to display what you want.

When ready to connect to your flashed M-Vision change the following in appsettings.json so it connects:
"sendToDevice": true,
"mode": "run",

Leave the rest as it is or customize to your needs

Then start in the program. 



Currently only English and Chinese are supported. Errors/logs are currently only in english
If one does want to expand on the compatable languages, the main addition needs to be in MainWindow.xaml.cs and adding the language letter code in UiStrings.cs